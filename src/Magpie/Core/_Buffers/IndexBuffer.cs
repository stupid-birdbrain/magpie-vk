using Magpie.Graphics;
using Vortice.Vulkan;

namespace Magpie.Core;

public unsafe struct IndexBuffer : IDisposable {
    public Buffer Buffer;
    public DeviceMemory Memory;
    public readonly uint IndexCount;
    public readonly VkIndexType IndexType;

    public IndexBuffer(LogicalDevice logicalDevice, CmdPool commandPool, Queue graphicsQueue, ReadOnlySpan<byte> indexDataBytes, VkIndexType indexType = VkIndexType.Uint32, VkBufferUsageFlags extraUsageFlags = VkBufferUsageFlags.None) {
        Buffer = default;
        Memory = default;
        IndexCount = 0;
        IndexType = indexType;

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
        
        Buffer stagingBuffer = new(logicalDevice, totalIndexBufferSize, VkBufferUsageFlags.TransferSrc);
        DeviceMemory stagingMemory = new(stagingBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);

        stagingMemory.CopyFrom(indexDataBytes);

        Buffer = new Buffer(logicalDevice, totalIndexBufferSize, VkBufferUsageFlags.TransferDst | VkBufferUsageFlags.IndexBuffer | extraUsageFlags);
        Memory = new DeviceMemory(Buffer, VkMemoryPropertyFlags.DeviceLocal);

        {
            using var fenceLease = new Fence(logicalDevice, VkFenceCreateFlags.None);
            var copyCmd = commandPool.CreateCommandBuffer();

            copyCmd.Begin();
            
            VkBufferCopy copyRegion = new() { dstOffset = 0, srcOffset = 0, size = totalIndexBufferSize };
            Vulkan.vkCmdCopyBuffer(copyCmd, stagingBuffer, Buffer, 1, &copyRegion);

            copyCmd.End();

            graphicsQueue.Submit(copyCmd, fenceLease); 
            fenceLease.Wait(); 

            copyCmd.Dispose();
        }

        stagingMemory.Dispose();
        stagingBuffer.Dispose();
    }

    public void Dispose() {
        Memory.Dispose();
        Buffer.Dispose();
    }

    public static implicit operator VkBuffer(IndexBuffer ib) => ib.Buffer.Value;
}