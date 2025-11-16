using Vortice.Vulkan;

namespace Magpie.Core;

public struct Buffer {
    public LogicalDevice Device;
    internal VkBuffer Value;

    public Buffer() {
        
    }
    
    public static implicit operator VkBuffer(Buffer b) => b.Value;
}