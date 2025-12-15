using Auklet.Core;
using Vortice.Vulkan;

namespace Auklet;

public unsafe struct ImageView : IDisposable{
    public readonly VkImageAspectFlags Aspects;
    public readonly LogicalDevice Device;

    internal VkImageView Value;

    public ImageView(Image image, VkImageAspectFlags aspects = VkImageAspectFlags.Color, bool isCubemap = false) {
        uint mipLevels = 1;
        Aspects = aspects;
        Device = image.Device;
        VkImageViewType viewType = isCubemap ? VkImageViewType.ImageCube : VkImageViewType.Image2D;
        VkImageSubresourceRange range = new(aspects, 0, mipLevels, 0, isCubemap ? 6u : 1u);
        VkImageViewCreateInfo imageCreateInfo = new(image, viewType, image.Format, VkComponentMapping.Rgba, range);
        var result = Vulkan.vkCreateImageView(Device, &imageCreateInfo, null, out Value);
    }
    
    public void Dispose() {
        Vulkan.vkDestroyImageView(Device, Value);
        Value = default;
    }
}