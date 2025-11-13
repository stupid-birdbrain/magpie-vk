using Vortice.Vulkan;

namespace Magpie.Core;

public unsafe struct Swapchain :  IDisposable {
    public LogicalDevice Device;
    public uint Width;
    public uint Height;
    public VkFormat Format;
    
    internal VkSwapchainKHR Value;
    
    [Obsolete]
    public Swapchain() { throw new NotSupportedException("default constructor is not supported on swapchains!"); }

    public Swapchain(LogicalDevice device, uint width, uint height, Surface surface) {
        Device = device;
        Width = width;
        Height = height;
        
        var info = surface.GetSwapchainDescription(device.PhysicalDevice);

        var format = info.ChooseSwapSurfaceFormat();
        var presentMode = info.ChooseSwapPresentMode();
        Format = format.format;
        
        uint imageCount = info.Capabilities.minImageCount + 1;
        if (info.Capabilities.maxImageCount > 0 && imageCount > info.Capabilities.maxImageCount) {
            imageCount = info.Capabilities.maxImageCount;
        }
        
        VkSwapchainCreateInfoKHR swapchainCreateInfo = new() {
            sType = VkStructureType.SwapchainCreateInfoKHR,
            surface = surface,

            minImageCount = imageCount,
            imageFormat = format.format,
            imageColorSpace = format.colorSpace,
            imageExtent = new(width, height),
            imageArrayLayers = 1,
            imageUsage = VkImageUsageFlags.ColorAttachment
        };
        
        var queueFamilies = device.PhysicalDevice.FindQueueFamilies(surface);
        uint graphicsQueueFamily = queueFamilies.GraphicsFamily!.Value;
        uint presentQueueFamily = queueFamilies.PresentFamily!.Value;

        if (graphicsQueueFamily != presentQueueFamily) {
            uint* queueFamilyIndices = stackalloc uint[] { graphicsQueueFamily, presentQueueFamily };
            swapchainCreateInfo.imageSharingMode = VkSharingMode.Concurrent;
            swapchainCreateInfo.queueFamilyIndexCount = 2;
            swapchainCreateInfo.pQueueFamilyIndices = queueFamilyIndices;
        }
        
        swapchainCreateInfo.preTransform = info.Capabilities.currentTransform;
        swapchainCreateInfo.compositeAlpha = VkCompositeAlphaFlagsKHR.Opaque;
        swapchainCreateInfo.presentMode = presentMode;
        swapchainCreateInfo.clipped = true;
        
        var swapchainResult = Vulkan.vkCreateSwapchainKHR(device, &swapchainCreateInfo, null, out Value);
        if (swapchainResult != VkResult.Success) {
            throw new Exception($"failed to create swapchain! {swapchainResult}");
        }
        
        Console.WriteLine($"swapchain created!: {Width}x{Height}, {Format}");
    }

    public void Dispose() {
        Vulkan.vkDestroySwapchainKHR(Device, Value, null);
    }
}