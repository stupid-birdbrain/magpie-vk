using Magpie.Core;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.Vulkan;

namespace Magpie.Core;

public unsafe struct IndexBuffer : IDisposable {
    public Buffer Buffer;
    public DeviceMemory Memory;
    public uint IndexCount;
    public readonly VkIndexType IndexType;

    public IndexBuffer(LogicalDevice logicalDevice, CmdPool commandPool, Queue graphicsQueue, ReadOnlySpan<byte> indexDataBytes, VkIndexType indexType = VkIndexType.Uint32, VkBufferUsageFlags extraUsageFlags = VkBufferUsageFlags.None) {
        IndexCount = 0;
        IndexType = indexType;
        Buffer = default;
        Memory = default;

        uint totalIndexBufferSize = (uint)indexDataBytes.Length;

        switch (indexType) {
            case VkIndexType.Uint16:
                IndexCount = totalIndexBufferSize / sizeof(ushort);
                break;
            case VkIndexType.Uint32:
                IndexCount = totalIndexBufferSize / sizeof(uint);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(indexType), "unsupported index type!");
        }
        
        using Buffer stagingBuffer = new(logicalDevice, totalIndexBufferSize, VkBufferUsageFlags.TransferSrc);
        using DeviceMemory stagingMemory = new(stagingBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);

        stagingMemory.CopyFrom(indexDataBytes);

        Buffer = new Buffer(logicalDevice, totalIndexBufferSize, VkBufferUsageFlags.TransferDst | VkBufferUsageFlags.IndexBuffer | extraUsageFlags);
        Memory = new DeviceMemory(Buffer, VkMemoryPropertyFlags.DeviceLocal);

        using (var fenceLease = new Fence(logicalDevice, VkFenceCreateFlags.None)) {
            using var copyCmd = commandPool.CreateCommandBuffer();
            copyCmd.Begin();
            
            VkBufferCopy copyRegion = new() { dstOffset = 0, srcOffset = 0, size = totalIndexBufferSize };
            Vulkan.vkCmdCopyBuffer(copyCmd, stagingBuffer, Buffer, 1, &copyRegion);

            copyCmd.End();

            graphicsQueue.Submit(copyCmd, fenceLease); 
            fenceLease.Wait(); 
        }
    }

    public IndexBuffer(LogicalDevice logicalDevice, uint initialSize, VkBufferUsageFlags extraUsageFlags = VkBufferUsageFlags.None, VkIndexType indexType = VkIndexType.Uint32) {
        Buffer = new Buffer(logicalDevice, initialSize, VkBufferUsageFlags.IndexBuffer | extraUsageFlags);
        Memory = new DeviceMemory(Buffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        IndexCount = 0;
        IndexType = indexType;
    }

    public void CopyFrom<T>(ReadOnlySpan<T> data) where T : unmanaged {
        CopyFrom(data, 0);
    }

    public void CopyFrom<T>(ReadOnlySpan<T> data, uint startIndex) where T : unmanaged {
        uint elementSize = (uint)Unsafe.SizeOf<T>();
        nuint byteOffset = (nuint)(startIndex * elementSize);
        Memory.CopyFrom(MemoryMarshal.Cast<T, byte>(data), byteOffset);

        uint endIndex = startIndex + (uint)data.Length;
        if (endIndex > IndexCount) {
            IndexCount = endIndex;
        }
    }

    public void Resize(LogicalDevice logicalDevice, uint newSize, VkBufferUsageFlags extraUsageFlags) {
        if (newSize == Buffer.Size) return;
        
        Memory.Dispose();
        Buffer.Dispose();

        Buffer = new Buffer(logicalDevice, newSize, VkBufferUsageFlags.IndexBuffer | extraUsageFlags);
        Memory = new DeviceMemory(Buffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
    }

    public void Dispose() {
        Memory.Dispose();
        Buffer.Dispose();
    }

    public static implicit operator VkBuffer(IndexBuffer ib) => ib.Buffer.Value;
}