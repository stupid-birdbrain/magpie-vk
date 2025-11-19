using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie.Core;

public unsafe struct Buffer : IDisposable {
    public readonly LogicalDevice Device;
    internal VkBuffer Value;

    public readonly uint Size;
    public readonly VkBufferUsageFlags Usage;
    public readonly VkSharingMode SharingMode;

    public Buffer(LogicalDevice device, uint size, VkBufferUsageFlags usage, VkSharingMode sharingMode = VkSharingMode.Exclusive) {
        Device = device;
        Size = size;
        Usage = usage;
        SharingMode = sharingMode;

        var bufferInfo = new VkBufferCreateInfo {
            sType = VkStructureType.BufferCreateInfo,
            size = size,
            usage = usage,
            sharingMode = sharingMode
        };

        vkCreateBuffer(Device, &bufferInfo, null, out Value).CheckResult("could not create buffer!");
    }

    public void Dispose() {
        if (Value != VkBuffer.Null) {
            vkDestroyBuffer(Device, Value, null);
            Value = VkBuffer.Null;
        }
    }

    public static implicit operator VkBuffer(Buffer b) => b.Value;
}