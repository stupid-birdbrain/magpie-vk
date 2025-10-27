using Magpie.Core;
using Vortice.Vulkan;

namespace Magpie.Graphics;

public sealed class GraphicsDevice {
    public readonly VulkanInstance Instance;
    
    private readonly object _commandLock = new object();
    private readonly object _queueLock = new object();

    public GraphicsDevice() {
        
    }
}