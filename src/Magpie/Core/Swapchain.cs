using Vortice.Vulkan;

namespace Magpie.Core;

public unsafe struct Swapchain :  IDisposable {
    public LogicalDevice Device;
    public uint Width;
    public uint Height;
    public VkFormat Format;
    
    internal VkSwapchainKHR Value;

    internal VkImage[] Images;
    internal VkImageView[] ImageViews;
    
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    [Obsolete("default constructor is not supported on swapchains", error: true)] public Swapchain() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

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
            surface = surface.Value,

            minImageCount = imageCount,
            imageFormat = format.format,
            imageColorSpace = format.colorSpace,
            imageExtent = new(width, height),
            imageArrayLayers = 1,
            imageUsage = VkImageUsageFlags.ColorAttachment
        };
        
        var queueFamilies = device.PhysicalDevice.FindQueueFamilies(surface.Value);
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
        
        Images = Vulkan.vkGetSwapchainImagesKHR(device, Value).ToArray();
        ImageViews = new VkImageView[Images.Length];
        
        for (int i = 0; i < Images.Length; i++) {
            VkImageViewCreateInfo viewInfo = new() {
                sType = VkStructureType.ImageViewCreateInfo,
                image = Images[i],
                viewType = VkImageViewType.Image2D,
                format = Format,
                subresourceRange = new VkImageSubresourceRange
                {
                    aspectMask = VkImageAspectFlags.Color,
                    baseMipLevel = 0,
                    levelCount = 1,
                    baseArrayLayer = 0,
                    layerCount = 1
                }
            };
            Vulkan.vkCreateImageView(device, &viewInfo, null, out ImageViews[i]);
            Console.WriteLine($"imageview info: {viewInfo.format}, {viewInfo.viewType}");
        }
        
        Console.WriteLine($"swapchain created!: {Width}x{Height}, {Format}, images: {Images.Length}");
    }

    public void Dispose() {
        for (int i = 0; i < ImageViews.Length; i++) {
            Vulkan.vkDestroyImageView(Device, ImageViews[i], null);
        }
        Vulkan.vkDestroySwapchainKHR(Device, Value, null);
    }
}