using Vortice.Vulkan;

namespace Magpie.Graphics;

public readonly unsafe struct Queue {
    public readonly VkDevice LogicalDevice;
    internal readonly VkQueue Value;

    public readonly uint Index;
    public readonly uint FamilyIndex;

    public Queue(VkDevice logicalDevice, uint index, uint familyIndex) {
        Index = index;
        FamilyIndex = familyIndex;
        LogicalDevice = logicalDevice;
        
        Vulkan.vkGetDeviceQueue(LogicalDevice, FamilyIndex, Index, out Value);
    }

    public readonly void Submit() {
        
    }
    
    public readonly VkResult TryPresent(Semaphore semaphore, VkSwapchainKHR swapchain, uint imageIndex) 
        => Vulkan.vkQueuePresentKHR(Value, semaphore.Value, swapchain, imageIndex);

    public readonly void Wait() {
        var result = Vulkan.vkQueueWaitIdle(Value);
        if(result != VkResult.Success)
            throw new Exception($"failed to wait for queue to idle!: {result}");
    }
}