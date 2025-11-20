using Magpie.Graphics;
using Standard;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie.Core;

/// <summary>
///     Records commands to be submitted to a queue.
/// </summary>
public unsafe struct CmdBuffer : IDisposable {
    public CmdPool Pool;
    internal VkCommandBuffer Value;
    
    public CmdBuffer(CmdPool cmdPool, VkCommandBuffer value) {
        Pool = cmdPool;
        Value = value;
    }
    
    public unsafe void Begin(VkCommandBufferUsageFlags flags = VkCommandBufferUsageFlags.OneTimeSubmit) {
        VkCommandBufferBeginInfo beginInfo = new()
        {
            sType = VkStructureType.CommandBufferBeginInfo,
            flags = flags
        };
        vkBeginCommandBuffer(Value, &beginInfo).CheckResult("could not begin cmd buffer!");
    }
    
    public void End() => vkEndCommandBuffer(Value).CheckResult("could not end cmd buffer!");

    public void Reset(VkCommandBufferResetFlags flags = 0) => vkResetCommandBuffer(Value, flags).CheckResult("could not reset cmd buffer!");
    
    public void SetViewport(Rectangle rect, float minDepth = 0f, float maxDepth = 1f) {
        VkViewport viewport = new(rect.X, rect.Y, rect.Width, rect.Height, minDepth, maxDepth);
        vkCmdSetViewport(Value, 0, 1, &viewport);
    }

    public void SetScissor(Rectangle rect) {
        VkRect2D rectValue = new((int)rect.X, (int)rect.Y, (uint)rect.Width, (uint)rect.Height);
        vkCmdSetScissor(Value, 0, 1, &rectValue);
    }
    
    public void Dispose() {
        if (Value != VkCommandBuffer.Null) {
            fixed(VkCommandBuffer* ptr = &Value)
                vkFreeCommandBuffers(Pool.Device, Pool.Value, 1, ptr);
            Value = VkCommandBuffer.Null;
        }
    }
    
    public static implicit operator VkCommandBuffer(CmdBuffer device) => device.Value;
}

public unsafe struct CmdPool {
    public readonly LogicalDevice Device;
    internal VkCommandPool Value;
    
    public unsafe CmdPool(LogicalDevice device, Queue queue) {
        Device = device;
        
        VkCommandPoolCreateFlags flags = VkCommandPoolCreateFlags.ResetCommandBuffer;
        
        VkCommandPoolCreateInfo createInfo = new() {
            sType = VkStructureType.CommandPoolCreateInfo,
            queueFamilyIndex = queue.FamilyIndex,
            flags = flags
        };
        
        vkCreateCommandPool(Device, &createInfo, null, out Value);
    }
    
    public readonly CmdBuffer CreateCommandBuffer(bool isPrimary = true) {
        VkCommandBufferAllocateInfo commandBufferAllocateInfo = new() {
            commandPool = Value,
            level = isPrimary ? VkCommandBufferLevel.Primary : VkCommandBufferLevel.Secondary,
            commandBufferCount = 1
        };

        var result = vkAllocateCommandBuffer(Device, &commandBufferAllocateInfo, out var newBuffer);

        return new CmdBuffer(this, newBuffer);
    }
    
    public void Dispose() {
        vkDestroyCommandPool(Device, Value, null);
        Value = default;
    }
}