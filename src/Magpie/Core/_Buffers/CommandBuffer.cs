using Magpie.Graphics;
using Vortice.Vulkan;

namespace Magpie.Core;

/// <summary>
///     Records commands to be submitted to a queue.
/// </summary>
public struct CommandBuffer : IDisposable {
    public CommandPool Pool;
    internal VkCommandBuffer Value;
    
    public CommandBuffer(CommandPool commandPool, VkCommandBuffer value) {
        Pool = commandPool;
        Value = value;
    }
    
    public unsafe void Begin(VkCommandBufferUsageFlags flags = VkCommandBufferUsageFlags.OneTimeSubmit) {
        VkCommandBufferBeginInfo beginInfo = new()
        {
            sType = VkStructureType.CommandBufferBeginInfo,
            flags = flags
        };
        Vulkan.vkBeginCommandBuffer(Value, &beginInfo);
    }

    public void End() {
        Vulkan.vkEndCommandBuffer(Value);
    }

    public void Reset(VkCommandBufferResetFlags flags = 0) {
        Vulkan.vkResetCommandBuffer(Value, flags);
    }
    
    public void Dispose() {
        
    }
    
    public static implicit operator VkCommandBuffer(CommandBuffer device) => device.Value;
}

public unsafe struct CommandPool {
    public readonly LogicalDevice Device;
    internal VkCommandPool Value;
    
    public unsafe CommandPool(LogicalDevice device, Queue queue) {
        Device = device;
        
        VkCommandPoolCreateFlags flags = VkCommandPoolCreateFlags.ResetCommandBuffer;
        
        VkCommandPoolCreateInfo createInfo = new() {
            sType = VkStructureType.CommandPoolCreateInfo,
            queueFamilyIndex = queue.FamilyIndex,
            flags = flags
        };
        
        Vulkan.vkCreateCommandPool(Device, &createInfo, null, out Value);
    }
    
    public readonly CommandBuffer CreateCommandBuffer(bool isPrimary = true) {
        VkCommandBufferAllocateInfo commandBufferAllocateInfo = new() {
            commandPool = Value,
            level = isPrimary ? VkCommandBufferLevel.Primary : VkCommandBufferLevel.Secondary,
            commandBufferCount = 1
        };

        var result = Vulkan.vkAllocateCommandBuffer(Device, &commandBufferAllocateInfo, out var newBuffer);

        return new CommandBuffer(this, newBuffer);
    }
    
    public void Dispose() {
        Vulkan.vkDestroyCommandPool(Device, Value, null);
        Value = default;
    }
}