using Standard;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie.Core;

/// <summary>
///     Records commands to be submitted to a queue.
/// </summary>
public unsafe struct CmdBuffer : IDisposable {
    public CmdPool Pool;
    internal VkCommandBuffer Value;
    
    public CmdBuffer(CmdPool cmdPool, VkCommandBuffer value) {
        Pool = cmdPool;
        Value = value;
    }
    
    public unsafe void Begin(VkCommandBufferUsageFlags flags = VkCommandBufferUsageFlags.OneTimeSubmit) {
        VkCommandBufferBeginInfo beginInfo = new()
        {
            sType = VkStructureType.CommandBufferBeginInfo,
            flags = flags
        };
        vkBeginCommandBuffer(Value, &beginInfo).CheckResult("could not begin cmd buffer!");
    }
    
    public void End() => vkEndCommandBuffer(Value).CheckResult("could not end cmd buffer!");

    public void Reset(VkCommandBufferResetFlags flags = 0) => vkResetCommandBuffer(Value, flags).CheckResult("could not reset cmd buffer!");
    
    public void SetViewport(Rectangle rect, float minDepth = 0f, float maxDepth = 1f) {
        VkViewport viewport = new(rect.X, rect.Y, rect.Width, rect.Height, minDepth, maxDepth);
        vkCmdSetViewport(Value, 0, 1, &viewport);
    }

    public void SetScissor(Rectangle rect) {
        VkRect2D rectValue = new((int)rect.X, (int)rect.Y, (uint)rect.Width, (uint)rect.Height);
        vkCmdSetScissor(Value, 0, 1, &rectValue);
    }
    
    public void CopyBufferToImage(Buffer buffer, Image image, uint width, uint height, uint srcOffset, uint mipLevel, uint layerCount = 1) {
        VkBufferImageCopy region = new() {
            bufferOffset = srcOffset,
            bufferRowLength = 0,
            bufferImageHeight = 0,
            imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, mipLevel, 0, layerCount),
            imageOffset = new VkOffset3D(0, 0, 0),
            imageExtent = new VkExtent3D(width, height, 1)
        };

        vkCmdCopyBufferToImage(Value, buffer, image, VkImageLayout.TransferDstOptimal, 1, &region);
    }
    
    public void BindPipeline(Pipeline pipeline) 
        => vkCmdBindPipeline(Value, VkPipelineBindPoint.Graphics, pipeline);
    
    public void BindVertexBuffer<TVertex>(in VertexBuffer<TVertex> vertexBuffer, uint firstBinding = 0, ulong offset = 0) where TVertex : unmanaged {
        var bufferHandle = vertexBuffer.Buffer.Value;
        vkCmdBindVertexBuffers(Value, firstBinding, 1, &bufferHandle, &offset);
    }

    public void BindIndexBuffer(in IndexBuffer indexBuffer, ulong offset = 0, VkIndexType indexType = VkIndexType.Uint32) 
        => vkCmdBindIndexBuffer(Value, indexBuffer.Buffer.Value, offset, indexType);
    
    public readonly void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0)
        => vkCmdDrawIndexed(Value, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    
    public readonly void BindDescriptorSets(PipelineLayout layout, Span<DescriptorSet> descriptorSets, uint set = 0) {
        var descriptorSetValue = stackalloc VkDescriptorSet[descriptorSets.Length];
        for (int i = 0; i < descriptorSets.Length; i++) {
            descriptorSetValue[i] = descriptorSets[i].Value;
        }

        vkCmdBindDescriptorSets(
            Value, 
            VkPipelineBindPoint.Graphics, 
            layout.Value, 
            set, 
            new ReadOnlySpan<VkDescriptorSet>(descriptorSetValue, descriptorSets.Length)
            );
    }
    
    public void TransitionImageLayout(Image image, VkImageLayout oldLayout, VkImageLayout newLayout, VkImageAspectFlags aspects = VkImageAspectFlags.Color, uint baseMipLevel = 0, uint levelCount = 1, uint baseArrayLayer = 0, uint layerCount = 1) {
        VkImageMemoryBarrier barrier = new() {
            sType = VkStructureType.ImageMemoryBarrier,
            oldLayout = oldLayout,
            newLayout = newLayout,
            srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            image = image,
            subresourceRange = new VkImageSubresourceRange(aspects, baseMipLevel, levelCount, baseArrayLayer, layerCount)
        };

        VkPipelineStageFlags sourceStage;
        VkPipelineStageFlags destinationStage;

        if (oldLayout == VkImageLayout.Undefined && newLayout == VkImageLayout.TransferDstOptimal)
        {
            barrier.srcAccessMask = 0;
            barrier.dstAccessMask = VkAccessFlags.TransferWrite;
            sourceStage = VkPipelineStageFlags.TopOfPipe;
            destinationStage = VkPipelineStageFlags.Transfer;
        }
        else if (oldLayout == VkImageLayout.TransferDstOptimal && newLayout == VkImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.srcAccessMask = VkAccessFlags.TransferWrite;
            barrier.dstAccessMask = VkAccessFlags.ShaderRead;
            sourceStage = VkPipelineStageFlags.Transfer;
            destinationStage = VkPipelineStageFlags.FragmentShader;
        }
        else if (oldLayout == VkImageLayout.Undefined && newLayout == VkImageLayout.DepthStencilAttachmentOptimal)
        {
            barrier.srcAccessMask = 0;
            barrier.dstAccessMask = VkAccessFlags.DepthStencilAttachmentRead | VkAccessFlags.DepthStencilAttachmentWrite;
            sourceStage = VkPipelineStageFlags.TopOfPipe;
            destinationStage = VkPipelineStageFlags.EarlyFragmentTests;
        }
        else if (oldLayout == VkImageLayout.ColorAttachmentOptimal && newLayout == VkImageLayout.PresentSrcKHR)
        {
            barrier.srcAccessMask = VkAccessFlags.ColorAttachmentWrite;
            barrier.dstAccessMask = 0;
            sourceStage = VkPipelineStageFlags.ColorAttachmentOutput;
            destinationStage = VkPipelineStageFlags.BottomOfPipe;
        }
        else if (oldLayout == VkImageLayout.Undefined && newLayout == VkImageLayout.ColorAttachmentOptimal)
        {
            barrier.srcAccessMask = 0;
            barrier.dstAccessMask = VkAccessFlags.ColorAttachmentWrite;
            sourceStage = VkPipelineStageFlags.TopOfPipe;
            destinationStage = VkPipelineStageFlags.ColorAttachmentOutput;
        }
        else if (oldLayout == VkImageLayout.TransferDstOptimal && newLayout == VkImageLayout.TransferSrcOptimal)
        {
            barrier.srcAccessMask = VkAccessFlags.TransferWrite;
            barrier.dstAccessMask = VkAccessFlags.TransferRead;
            sourceStage = VkPipelineStageFlags.Transfer;
            destinationStage = VkPipelineStageFlags.Transfer;
        }
        else if (oldLayout == VkImageLayout.TransferSrcOptimal && newLayout == VkImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.srcAccessMask = VkAccessFlags.TransferRead;
            barrier.dstAccessMask = VkAccessFlags.ShaderRead;
            sourceStage = VkPipelineStageFlags.Transfer;
            destinationStage = VkPipelineStageFlags.FragmentShader;
        }
        else {
            barrier.srcAccessMask = VkAccessFlags.MemoryRead | VkAccessFlags.MemoryWrite;
            barrier.dstAccessMask = VkAccessFlags.MemoryRead | VkAccessFlags.MemoryWrite;
            sourceStage = VkPipelineStageFlags.AllCommands;
            destinationStage = VkPipelineStageFlags.AllCommands;
        }

        vkCmdPipelineBarrier(Value, sourceStage, destinationStage, 0, 0, null, 0, null, 1, &barrier);
    }
    

    public void BufferMemoryBarrier(
        VkAccessFlags srcAccessMask,
        VkAccessFlags dstAccessMask,
        Buffer bufferA,
        Buffer bufferB,
        VkPipelineStageFlags srcStageMask,
        VkPipelineStageFlags dstStageMask)
    {
        VkBufferMemoryBarrier* barriers = stackalloc VkBufferMemoryBarrier[2];

        barriers[0] = new VkBufferMemoryBarrier {
            sType = VkStructureType.BufferMemoryBarrier,
            srcAccessMask = srcAccessMask,
            dstAccessMask = dstAccessMask,
            srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            buffer = bufferA,
            offset = 0,
            size = VK_WHOLE_SIZE
        };

        barriers[1] = new VkBufferMemoryBarrier {
            sType = VkStructureType.BufferMemoryBarrier,
            srcAccessMask = srcAccessMask,
            dstAccessMask = dstAccessMask,
            srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            buffer = bufferB,
            offset = 0,
            size = VK_WHOLE_SIZE
        };

        vkCmdPipelineBarrier(Value, srcStageMask, dstStageMask, 0, 0, null, 2, barriers, 0, null);
    }

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ) {
        vkCmdDispatch(Value, groupCountX, groupCountY, groupCountZ);
    }
    
    public void Dispose() {
        if (Value != VkCommandBuffer.Null) {
            fixed(VkCommandBuffer* ptr = &Value)
                vkFreeCommandBuffers(Pool.Device, Pool.Value, 1, ptr);
            Value = VkCommandBuffer.Null;
        }
    }
    
    public static implicit operator VkCommandBuffer(CmdBuffer device) => device.Value;
}

public unsafe struct CmdPool {
    public readonly LogicalDevice Device;
    internal VkCommandPool Value;
    
    public unsafe CmdPool(LogicalDevice device, Queue queue) {
        Device = device;
        
        VkCommandPoolCreateFlags flags = VkCommandPoolCreateFlags.ResetCommandBuffer;
        
        VkCommandPoolCreateInfo createInfo = new() {
            sType = VkStructureType.CommandPoolCreateInfo,
            queueFamilyIndex = queue.FamilyIndex,
            flags = flags
        };
        
        vkCreateCommandPool(Device, &createInfo, null, out Value);
    }
    
    public readonly CmdBuffer CreateCommandBuffer(bool isPrimary = true) {
        VkCommandBufferAllocateInfo commandBufferAllocateInfo = new() {
            commandPool = Value,
            level = isPrimary ? VkCommandBufferLevel.Primary : VkCommandBufferLevel.Secondary,
            commandBufferCount = 1
        };

        var result = vkAllocateCommandBuffer(Device, &commandBufferAllocateInfo, out var newBuffer);

        return new CmdBuffer(this, newBuffer);
    }
    
    public void Dispose() {
        vkDestroyCommandPool(Device, Value, null);
        Value = default;
    }
}