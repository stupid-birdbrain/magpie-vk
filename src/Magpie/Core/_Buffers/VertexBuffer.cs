using Magpie.Graphics;
using Vortice.Vulkan;

namespace Magpie.Core;

public unsafe struct VertexBuffer : IDisposable {
    public Buffer Buffer;
    public DeviceMemory Memory;
    public readonly uint VertexCount;
    public readonly uint VertexSizeInBytes;
    
    public VertexBuffer(LogicalDevice logicalDevice, CmdPool commandPool, Queue graphicsQueue, ReadOnlySpan<byte> vertexDataBytes, uint vertexSizeInBytes, VkBufferUsageFlags extraUsageFlags = VkBufferUsageFlags.None) {
        Buffer = default;
        Memory = default;
        VertexCount = 0;
        VertexSizeInBytes = vertexSizeInBytes;

        uint totalVertexBufferSize = (uint)vertexDataBytes.Length;

        VertexCount = totalVertexBufferSize / vertexSizeInBytes;

        Buffer stagingBuffer = new(logicalDevice, totalVertexBufferSize, VkBufferUsageFlags.TransferSrc);
        DeviceMemory stagingMemory = new(stagingBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);

        stagingMemory.CopyFrom(vertexDataBytes);

        Buffer = new Buffer(logicalDevice, totalVertexBufferSize, VkBufferUsageFlags.TransferDst | VkBufferUsageFlags.VertexBuffer | extraUsageFlags);
        Memory = new DeviceMemory(Buffer, VkMemoryPropertyFlags.DeviceLocal);

        {
            using var fenceLease = new Fence(logicalDevice, VkFenceCreateFlags.None);
            var copyCmd = commandPool.CreateCommandBuffer();

            copyCmd.Begin();
            
            VkBufferCopy copyRegion = new() { dstOffset = 0, srcOffset = 0, size = totalVertexBufferSize };
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

    public static implicit operator VkBuffer(VertexBuffer vb) => vb.Buffer.Value;
}