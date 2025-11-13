using Magpie.Core;
using Vortice.Vulkan;

namespace Magpie.Graphics;

public readonly unsafe struct Queue {
    public readonly LogicalDevice LogicalDevice;
    internal readonly VkQueue Value;

    public readonly uint Index;
    public readonly uint FamilyIndex;

    public Queue(LogicalDevice logicalDevice, uint index, uint familyIndex) {
        Index = index;
        FamilyIndex = familyIndex;
        LogicalDevice = logicalDevice;
        
        Vulkan.vkGetDeviceQueue(LogicalDevice, FamilyIndex, Index, out Value);
    }

    public readonly void Submit() {
        
    }
    
    public readonly VkResult TryPresent(Semaphore semaphore, Swapchain swapchain, uint imageIndex) 
        => Vulkan.vkQueuePresentKHR(Value, semaphore.Value, swapchain.Value, imageIndex);

    public readonly void Wait() {
        var result = Vulkan.vkQueueWaitIdle(Value);
        if(result != VkResult.Success)
            throw new Exception($"failed to wait for queue to idle!: {result}");
    }
}