using Vortice.Vulkan;

namespace Magpie.Core;

public unsafe struct Image : IDisposable {
    public readonly LogicalDevice Device;
    public readonly uint Width;
    public readonly uint Height;
    public readonly VkFormat Format;

    internal VkImage Value;

    public Image(LogicalDevice device, uint width, uint height, uint depth, VkFormat format, VkImageUsageFlags flags) {
        Device = device;
        Width = width;
        Height = height;
        Format = format;

        var createInfo = new VkImageCreateInfo
        {
            format = format,
            imageType = VkImageType.Image2D,
            
            extent = new VkExtent3D(width, height, depth),
            mipLevels = 1,
            arrayLayers = 1,
            usage = flags,
            flags = default,
            samples = VkSampleCountFlags.Count1,
            sharingMode = VkSharingMode.Exclusive
        };
        
        var result = Vulkan.vkCreateImage(Device, &createInfo, null, out Value);
        Vulkan.ThrowIfFailed(result, "failed to create image!");
    }
    
    internal Image(LogicalDevice logicalDevice, VkImage existingImage, uint width, uint height, VkFormat format) {
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