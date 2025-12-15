using Auklet.Core;
using Vortice.Vulkan;

namespace Auklet;

public sealed class RenderTarget : IDisposable {
    public Image Image { get; }
    public DeviceMemory Memory { get; }
    public ImageView ImageView { get; }
    public uint Width { get; }
    public uint Height { get; }
    public VkFormat Format { get; }
    internal VkImageLayout CurrentLayout { get; set; }

    internal RenderTarget(LogicalDevice device, CmdPool commandPool, Queue queue, uint width, uint height, VkFormat format) {
        Width = width;
        Height = height;
        Format = format;

        Image = new Image(device, width, height, 1, format, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled | VkImageUsageFlags.TransferDst | VkImageUsageFlags.TransferSrc);
        Memory = new DeviceMemory(Image, VkMemoryPropertyFlags.DeviceLocal);
        ImageView = new ImageView(Image);

        using var fence = new Fence(device, VkFenceCreateFlags.None);
        var cmd = commandPool.CreateCommandBuffer();
        cmd.Begin();
        cmd.TransitionImageLayout(Image, VkImageLayout.Undefined, VkImageLayout.ShaderReadOnlyOptimal);
        cmd.End();
        queue.Submit(cmd, fence);
        fence.Wait();
        cmd.Dispose();

        CurrentLayout = VkImageLayout.ShaderReadOnlyOptimal;
    }

    public SpriteTexture CreateSpriteTexture(VkFilter filter = VkFilter.Linear, VkSamplerAddressMode addressMode = VkSamplerAddressMode.ClampToEdge) {
        Sampler sampler = new(Image.Device, new SamplerCreateParameters(filter, addressMode));
        return new SpriteTexture(Image, Memory, ImageView, sampler, ownsImage: false, ownsMemory: false, ownsImageView: false, ownsSampler: true);
    }

    public void Dispose() {
        ImageView.Dispose();
        Memory.Dispose();
        Image.Dispose();
    }
}
