using Magpie.Core;
using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using Buffer = Magpie.Core.Buffer;

namespace Magpie.Core;

public unsafe struct VertexBuffer<TVertex> : IDisposable where TVertex : unmanaged {
    public Buffer Buffer;
    public DeviceMemory Memory;
    public readonly uint VertexCount;
    public readonly uint VertexSizeInBytes;

    public VertexBuffer(LogicalDevice logicalDevice, CmdPool commandPool, Queue graphicsQueue, ReadOnlySpan<TVertex> vertexData, VkBufferUsageFlags extraUsageFlags = VkBufferUsageFlags.None) {
        Buffer = default;
        Memory = default;
        VertexCount = 0;
        VertexSizeInBytes = 0;

        if (vertexData.IsEmpty) {
            throw new ArgumentException("vertex data cannot be empty!", nameof(vertexData));
        }
        
        VertexSizeInBytes = (uint)Unsafe.SizeOf<TVertex>();
        VertexCount = (uint)vertexData.Length;
        uint totalVertexBufferSize = VertexCount * VertexSizeInBytes;

        Buffer stagingBuffer = new(logicalDevice, totalVertexBufferSize, VkBufferUsageFlags.TransferSrc);
        DeviceMemory stagingMemory = new(stagingBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);

        stagingMemory.CopyFrom(vertexData);

        Buffer = new Buffer(logicalDevice, totalVertexBufferSize, VkBufferUsageFlags.TransferDst | VkBufferUsageFlags.VertexBuffer | extraUsageFlags);
        Memory = new DeviceMemory(Buffer, VkMemoryPropertyFlags.DeviceLocal);
        
        {
            using var fenceLease = new Fence(logicalDevice, VkFenceCreateFlags.None);
            var copyCmd = commandPool.CreateCommandBuffer();

            copyCmd.Begin();
            
            VkBufferCopy copyRegion = new() { dstOffset = 0, srcOffset = 0, size = totalVertexBufferSize };
            vkCmdCopyBuffer(copyCmd, stagingBuffer, Buffer, 1, &copyRegion);

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

    public static implicit operator VkBuffer(VertexBuffer<TVertex> vb) => vb.Buffer.Value;
}