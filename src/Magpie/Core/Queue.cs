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

    public readonly void Submit(
        CommandBuffer commandBuffer,
        Semaphore waitSemaphore,
        Semaphore signalSemaphore,
        Fence fence
    )
    {
        VkSubmitInfo submitInfo = new() { sType = VkStructureType.SubmitInfo };

        VkSemaphore waitSem = waitSemaphore.Value;
        VkPipelineStageFlags waitStage =
            VkPipelineStageFlags.ColorAttachmentOutput;
        submitInfo.waitSemaphoreCount = 1;
        submitInfo.pWaitSemaphores = &waitSem;
        submitInfo.pWaitDstStageMask = &waitStage;

        VkCommandBuffer cmd = commandBuffer.Value;
        submitInfo.commandBufferCount = 1;
        submitInfo.pCommandBuffers = &cmd;

        VkSemaphore signalSem = signalSemaphore.Value;
        submitInfo.signalSemaphoreCount = 1;
        submitInfo.pSignalSemaphores = &signalSem;

        VkFence f = fence.Value;
        var result = Vulkan.vkQueueSubmit(Value, 1, &submitInfo, f);
        if (result != VkResult.Success) {
            throw new Exception($"failed to submit to queue!: {result}");
        }
    }
    
    public readonly void Submit(CommandBuffer commandBuffer, Fence fence) {
        VkCommandBuffer cmd = commandBuffer.Value;
        VkSubmitInfo submitInfo = new() {
            sType = VkStructureType.SubmitInfo,
            commandBufferCount = 1,
            pCommandBuffers = &cmd
        };

        var result = Vulkan.vkQueueSubmit(Value, 1, &submitInfo, fence);
        if (result != VkResult.Success) {
            throw new Exception($"failed to submit to queue!: {result}");
        }
    }
    
    public readonly void Submit(CommandBuffer commandBuffer) {
        VkSubmitInfo submitInfo = new()
        {
            sType = VkStructureType.SubmitInfo,
            commandBufferCount = 1,
            pCommandBuffers = &commandBuffer.Value
        };
        Vulkan.vkQueueSubmit(Value, 1, &submitInfo, VkFence.Null);
    }
    
    public readonly VkResult TryPresent(Semaphore semaphore, Swapchain swapchain, uint imageIndex) 
        => Vulkan.vkQueuePresentKHR(Value, semaphore.Value, swapchain.Value, imageIndex);

    public readonly void Wait() {
        var result = Vulkan.vkQueueWaitIdle(Value);
        if(result != VkResult.Success)
            throw new Exception($"failed to wait for queue to idle!: {result}");
    }
}