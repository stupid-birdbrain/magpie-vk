using Vortice.Vulkan;

namespace Magpie.Graphics;

public unsafe readonly struct Semaphore : IDisposable {
    internal readonly VkSemaphore Value;
    public readonly bool IsDisposed => Value.IsNull;
    
    public readonly VkDevice LogicalDevice;

    public Semaphore(VkDevice logicalDevice) {
        this.LogicalDevice = logicalDevice;
        var result = Vulkan.vkCreateSemaphore(logicalDevice, out Value);
        if(result != VkResult.Success)
            throw new Exception($"failed to create semaphore!: {result}");
    }
    
    public void Dispose() {
        Vulkan.vkDestroySemaphore(LogicalDevice, Value);
    }
    
    public static implicit operator VkSemaphore(Semaphore semaphore) => semaphore.Value;
}