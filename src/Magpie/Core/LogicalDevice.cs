using Magpie.Graphics;
using Vortice.Vulkan;

namespace Magpie.Core;

public unsafe struct LogicalDevice : IDisposable {
    public readonly PhysicalDevice PhysicalDevice;

    internal readonly VkDevice Value;
    public readonly nint Address => Value.Handle;

    public LogicalDevice(PhysicalDevice physicalDevice) {
        PhysicalDevice = physicalDevice;
    }
    
    public void Dispose() {
        
    }
}