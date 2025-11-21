using Magpie.Utilities;
using Vortice.Vulkan;

namespace Magpie.Core;

public unsafe struct DescriptorSet : IDisposable {
    public DescriptorPool Pool;

    public VkDescriptorSet Value;
    
    internal DescriptorSet(DescriptorPool pool, VkDescriptorSet value) {
        Pool = pool;
        Value = value;
    }
    
    public readonly void Update(Buffer buffer, VkDescriptorType descriptorType, uint binding = 0) {
        VkDescriptorBufferInfo bufferInfo = new();
        bufferInfo.buffer = buffer;
        bufferInfo.offset = 0;
        bufferInfo.range = buffer.Size;

        Span<VkWriteDescriptorSet> descriptorWrite = stackalloc VkWriteDescriptorSet[1];
        descriptorWrite[0] = new() {
            dstSet = Value,
            dstBinding = binding,
            dstArrayElement = 0,
            descriptorType = descriptorType,
            descriptorCount = 1,
            pBufferInfo = &bufferInfo
        };

        Vulkan.vkUpdateDescriptorSets(Pool.Device, 1, descriptorWrite.GetPointer(), 0, null);
    }
    
    public readonly void Update(ImageView imageView, Sampler sampler, VkDescriptorType descriptorType, uint binding = 0) {
        VkDescriptorImageInfo imageInfo = new();
        imageInfo.imageView = imageView.Value;
        imageInfo.imageLayout = VkImageLayout.ShaderReadOnlyOptimal;
        imageInfo.sampler = sampler.Value;

        Span<VkWriteDescriptorSet> descriptorWrite = stackalloc VkWriteDescriptorSet[1];
        descriptorWrite[0] = new()
        {
            dstSet = Value,
            dstBinding = binding,
            dstArrayElement = 0,
            descriptorType = descriptorType,
            descriptorCount = 1,
            pImageInfo = &imageInfo
        };

        Vulkan.vkUpdateDescriptorSets(Pool.Device, 1, descriptorWrite.GetPointer(), 0, null);
    }
    
    public void Dispose() {
        Vulkan.vkFreeDescriptorSets(Pool.Device, Pool.Value, Value);
        Value = default;
    }
}

public unsafe struct DescriptorPool : IDisposable {
    public LogicalDevice Device;
    internal VkDescriptorPool Value;
    
    public DescriptorPool(LogicalDevice logicalDevice, VkDescriptorType descriptorType, uint descriptorCount, uint poolSizeCount, uint maxSets) {
        Device = logicalDevice;
        Span<VkDescriptorPoolSize> poolSizes = stackalloc VkDescriptorPoolSize[1] { new(descriptorType, descriptorCount) };
        VkDescriptorPoolCreateInfo createInfo = new()
        {
            poolSizeCount = poolSizeCount,
            pPoolSizes = poolSizes.GetPointer(),
            maxSets = maxSets,
            flags = VkDescriptorPoolCreateFlags.FreeDescriptorSet
        };

        Vulkan.vkCreateDescriptorPool(Device, &createInfo, null, out Value);
    }
    
    public DescriptorPool(LogicalDevice logicalDevice, ReadOnlySpan<DescriptorPoolSize> poolSizes, uint maxSets) {
        Device = logicalDevice;
        VkDescriptorPoolCreateInfo createInfo = new() {
            poolSizeCount = (uint)poolSizes.Length,
            pPoolSizes = (VkDescriptorPoolSize*)poolSizes.GetPointer(),
            maxSets = maxSets,
            flags = VkDescriptorPoolCreateFlags.FreeDescriptorSet
        };

        Vulkan.vkCreateDescriptorPool(Device, &createInfo, null, out Value);
    }
    
    public DescriptorSet AllocateDescriptorSet(DescriptorSetLayout layout) {
        VkDescriptorSetLayout setLayout = layout.Value;
        VkDescriptorSetAllocateInfo allocInfo = new()
        {
            sType = VkStructureType.DescriptorSetAllocateInfo,
            descriptorPool = Value,
            descriptorSetCount = 1,
            pSetLayouts = &setLayout
        };

        VkDescriptorSet descriptorSet;
        Vulkan.vkAllocateDescriptorSets(Device, &allocInfo, &descriptorSet).CheckResult("failed to allocate descriptor sets!");
        
        return new DescriptorSet(this, descriptorSet);
    }
    
    public void Dispose() {
        Vulkan.vkDestroyDescriptorPool(Device, Value);
        Value = default;
    }
}

public struct DescriptorPoolSize(VkDescriptorType type, uint count) {
    public VkDescriptorType Type = type;
    public uint Count = count;
}