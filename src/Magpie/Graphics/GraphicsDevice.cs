using Magpie.Core;
using Vortice.Vulkan;

using static Vortice.Vulkan.Vulkan;

namespace Magpie.Graphics;

public sealed class GraphicsDevice {
    public readonly VulkanInstance Instance;
    private Swapchain _mainSwapchain;
    private Surface _surface;

    private Queue _presentQueue;
    private Queue _graphicsQueue;
    
    private PhysicalDevice _physicalDevice;
    private LogicalDevice _logicalDevice;
    
    private readonly object _commandLock = new object();
    private readonly object _queueLock = new object();

    private int _frameIndex;

    public GraphicsDevice(VulkanInstance instance, Surface surface, PhysicalDevice physicalDevice, LogicalDevice logicalDevice) {
        Instance = instance;
        _surface = surface;
        _physicalDevice = physicalDevice;
        _logicalDevice = logicalDevice;
        
        var extent = _surface.ChooseSwapExtent(_physicalDevice);
        _mainSwapchain = new(_logicalDevice, extent.width, extent.height, _surface);
        Console.WriteLine($"main backbuffer swapchain created!:");
    }
}