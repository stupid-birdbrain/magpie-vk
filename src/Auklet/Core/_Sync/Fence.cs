using Vortice.Vulkan;

namespace Auklet.Core;

public unsafe struct Fence : IDisposable {
    internal VkFence Value;
    public readonly bool IsDisposed => Value.IsNull;

    public readonly VkDevice LogicalDevice;

    public Fence(VkDevice logicalDevice, VkFenceCreateFlags createFlags = VkFenceCreateFlags.Signaled) {
        LogicalDevice = logicalDevice;
        VkFenceCreateInfo fenceInfo = new() {
            sType = VkStructureType.FenceCreateInfo,
            flags = createFlags
        };
        
        var result = Vulkan.vkCreateFence(logicalDevice, &fenceInfo, null, out Value);
        if(result != VkResult.Success)
            throw new Exception($"failed to create fence!: {result}");
    }

    public readonly void Wait(ulong timeout = ulong.MaxValue) {
        fixed(VkFence* ptr = &Value)
            Vulkan.vkWaitForFences(LogicalDevice, 1, ptr, true, timeout);
    }
    
    public readonly void Reset() {
        fixed(VkFence* ptr = &Value)
            Vulkan.vkResetFences(LogicalDevice, 1, ptr);
    }
    
    public void Dispose() {
        if (!Value.IsNull) {
            Vulkan.vkDestroyFence(LogicalDevice, Value, null);
            Value = VkFence.Null;
        }
    }
    
    public static implicit operator VkFence(Fence fence) => fence.Value;
}