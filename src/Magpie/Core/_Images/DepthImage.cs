using Magpie.Core;
using Vortice.Vulkan;

namespace Magpie;

public unsafe struct DepthImage : IDisposable {
    public LogicalDevice Device;
    public Image Image;
    public DeviceMemory Memory;
    public ImageView ImageView;
    public VkFormat Format;

    public DepthImage(LogicalDevice device, uint width, uint height, CmdPool commandPool, Queue graphicsQueue) {
        Device = device;
        Format = commandPool.Device.GetDepthFormat();

        Image = new Image(
            device,
            width, height, 1,
            Format,
            VkImageUsageFlags.DepthStencilAttachment | VkImageUsageFlags.TransientAttachment
        );

        Memory = new DeviceMemory(Image, VkMemoryPropertyFlags.DeviceLocal);

        ImageView = new ImageView( Image, VkImageAspectFlags.Depth);
    }

    public void Dispose() {
        ImageView.Dispose();
        Memory.Dispose();
        Image.Dispose();
    }
}