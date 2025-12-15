using Vortice.Vulkan;

namespace Auklet.Core;

public unsafe struct Image : IDisposable {
    public readonly LogicalDevice Device;
    public readonly uint Width;
    public readonly uint Height;
    public readonly VkFormat Format;
    public readonly VkSampleCountFlags Samples;

    internal VkImage Value;

    public Image(LogicalDevice device, uint width, uint height, uint depth, VkFormat format, VkImageUsageFlags flags, VkSampleCountFlags samples = VkSampleCountFlags.Count1) {
        Device = device;
        Width = width;
        Height = height;
        Format = format;
        Samples = samples;

        var createInfo = new VkImageCreateInfo
        {
            format = format,
            imageType = VkImageType.Image2D,
            
            extent = new VkExtent3D(width, height, depth),
            mipLevels = 1,
            arrayLayers = 1,
            usage = flags,
            flags = default,
            samples = samples,
            sharingMode = VkSharingMode.Exclusive
        };
        
        var result = Vulkan.vkCreateImage(Device, &createInfo, null, out Value);
        Vulkan.ThrowIfFailed(result, "failed to create image!");
    }
    
    public Image(LogicalDevice logicalDevice, VkImage existingImage, uint width, uint height, VkFormat format) {
        Device = logicalDevice;
        Format = format;
        Width = width;
        Height = height;
        Value = existingImage;
    }
    
    public void Dispose() {
        Vulkan.vkDestroyImage(Device, Value, null);
        Value = default;
    }
    
    public static implicit operator VkImage(Image image) => image.Value;
}