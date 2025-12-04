using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie.Core;

public unsafe struct DeviceMemory : IDisposable {
    public readonly LogicalDevice Device;
    public readonly ulong Size;
    public readonly VkMemoryPropertyFlags Flags;

    internal VkDeviceMemory Value; 
    
    public DeviceMemory(Buffer buffer, VkMemoryPropertyFlags properties) {
        Device = buffer.Device;
        Flags = properties;

        vkGetBufferMemoryRequirements(Device, buffer, out VkMemoryRequirements memoryRequirements);
        Size = memoryRequirements.size; 

        uint memoryTypeIndex = Device.GetMemoryTypeIndex(memoryRequirements.memoryTypeBits, properties);

        VkMemoryAllocateInfo allocInfo = new()
        {
            sType = VkStructureType.MemoryAllocateInfo,
            allocationSize = memoryRequirements.size,
            memoryTypeIndex = memoryTypeIndex
        };

        vkAllocateMemory(Device, &allocInfo, null, out Value)
            .CheckResult("failed to allocate memory for buffer!");
        
        vkBindBufferMemory(Device, buffer, Value, 0)
            .CheckResult("failed to bind memory to buffer!");
    }
    
    public DeviceMemory(Image image, VkMemoryPropertyFlags properties) {
        Device = image.Device;
        vkGetImageMemoryRequirements(Device, image, out VkMemoryRequirements memoryRequirements);
        Size = memoryRequirements.size;

        VkMemoryAllocateInfo allocInfo = new();
        allocInfo.allocationSize = memoryRequirements.size;
        allocInfo.memoryTypeIndex = Device.GetMemoryTypeIndex(memoryRequirements.memoryTypeBits, properties);

        vkAllocateMemory(Device, &allocInfo, null, out Value).CheckResult("failed to allocate memory for image!");
        
        vkBindImageMemory(Device, image, Value, 0).CheckResult("failed to bind memory to image!");
    }
    
    public readonly Span<T> Map<T>(int length) where T : unmanaged {
        void* byteData;
        vkMapMemory(Device, Value, 0, Size, 0, &byteData).CheckResult("failed to map memory!");
        return new(byteData, length);
    }
    
    public readonly void CopyFrom<T>(Span<T> sourceData) where T : unmanaged {
        void* byteData;
        vkMapMemory(Device, Value, 0, Size, 0, &byteData).CheckResult();
        sourceData.CopyTo(new Span<T>(byteData, sourceData.Length));
        vkUnmapMemory(Device, Value);
    }
    
    public readonly void CopyFrom<T>(ReadOnlySpan<T> sourceData) where T : unmanaged {
        void* byteData;
        vkMapMemory(Device, Value, 0, Size, 0, &byteData).CheckResult("failed to map memory for CopyFrom!"); 
        sourceData.CopyTo(new (byteData, sourceData.Length));
        vkUnmapMemory(Device, Value);
    }
    
    public void Unmap() {
        vkUnmapMemory(Device, Value);
    }
    
    public void Dispose() {
        if (Value != VkDeviceMemory.Null) {
            vkFreeMemory(Device, Value, null);
            Value = VkDeviceMemory.Null;
        }
    }

    public static implicit operator VkDeviceMemory(DeviceMemory dm) => dm.Value;
}