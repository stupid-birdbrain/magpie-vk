using Auklet.Core;
using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using Buffer = Auklet.Core.Buffer;

namespace Auklet.Core;

public unsafe struct VertexBuffer<TVertex> : IDisposable where TVertex : unmanaged {
    public Buffer Buffer;
    public DeviceMemory Memory;
    public uint VertexCount;
    public readonly uint VertexSizeInBytes;

    public VertexBuffer(LogicalDevice logicalDevice, CmdPool commandPool, Queue graphicsQueue, ReadOnlySpan<TVertex> vertexData, VkBufferUsageFlags extraUsageFlags = VkBufferUsageFlags.None) {
        VertexCount = 0;
        VertexSizeInBytes = (uint)Unsafe.SizeOf<TVertex>();
        Buffer = default;
        Memory = default;

        if (vertexData.IsEmpty) {
            throw new ArgumentException("vertex data cannot be empty!", nameof(vertexData));
        }
        
        VertexCount = (uint)vertexData.Length;
        uint totalVertexBufferSize = VertexCount * VertexSizeInBytes;

        using Buffer stagingBuffer = new(logicalDevice, totalVertexBufferSize, VkBufferUsageFlags.TransferSrc);
        using DeviceMemory stagingMemory = new(stagingBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);

        stagingMemory.CopyFrom(vertexData);

        Buffer = new Buffer(logicalDevice, totalVertexBufferSize, VkBufferUsageFlags.TransferDst | VkBufferUsageFlags.VertexBuffer | extraUsageFlags);
        Memory = new DeviceMemory(Buffer, VkMemoryPropertyFlags.DeviceLocal);
        
        using (var fenceLease = new Fence(logicalDevice, VkFenceCreateFlags.None)) {
            using var copyCmd = commandPool.CreateCommandBuffer();
            copyCmd.Begin();
            
            VkBufferCopy copyRegion = new() { dstOffset = 0, srcOffset = 0, size = totalVertexBufferSize };
            vkCmdCopyBuffer(copyCmd, stagingBuffer, Buffer, 1, &copyRegion);

            copyCmd.End();

            graphicsQueue.Submit(copyCmd, fenceLease); 
            fenceLease.Wait(); 
        }
    }
    
    public VertexBuffer(LogicalDevice logicalDevice, uint initialSize, VkBufferUsageFlags extraUsageFlags = VkBufferUsageFlags.None) {
        Buffer = new Buffer(logicalDevice, initialSize, VkBufferUsageFlags.VertexBuffer | extraUsageFlags);
        Memory = new DeviceMemory(Buffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        VertexCount = 0;
        VertexSizeInBytes = (uint)Unsafe.SizeOf<TVertex>();
    }

    public void CopyFrom(ReadOnlySpan<TVertex> data) {
        CopyFrom(data, 0);
    }

    public void CopyFrom(ReadOnlySpan<TVertex> data, uint startVertex) {
        uint byteOffset = startVertex * VertexSizeInBytes;
        Memory.CopyFrom(data, byteOffset);
        uint endVertex = startVertex + (uint)data.Length;
        if (endVertex > VertexCount) {
            VertexCount = endVertex;
        }
    }

    public void Resize(LogicalDevice logicalDevice, uint newSize, VkBufferUsageFlags extraUsageFlags) {
        if (newSize == Buffer.Size) return;

        Memory.Dispose();
        Buffer.Dispose();

        Buffer = new Buffer(logicalDevice, newSize, VkBufferUsageFlags.VertexBuffer | extraUsageFlags);
        Memory = new DeviceMemory(Buffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
    }

    public void Dispose() {
        Memory.Dispose();
        Buffer.Dispose();
    }

    public static implicit operator VkBuffer(VertexBuffer<TVertex> vb) => vb.Buffer.Value;
}