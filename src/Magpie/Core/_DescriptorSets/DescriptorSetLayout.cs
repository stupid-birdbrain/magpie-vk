using Magpie.Utilities;
using Vortice.Vulkan;

namespace Magpie.Core;

public unsafe struct DescriptorSetLayoutBinding {
    public uint Binding;
    public VkDescriptorType DescriptorType;
    public uint DescriptorCount;
    public VkShaderStageFlags ShaderFlags;
    internal VkSampler* ImmutableSamplers;

    public DescriptorSetLayoutBinding(uint binding, VkDescriptorType descriptorType, uint descriptorCount, VkShaderStageFlags shaderFlags) {
        Binding = binding;
        DescriptorType = descriptorType;
        DescriptorCount = descriptorCount;
        ShaderFlags = shaderFlags;
        ImmutableSamplers = default;
    }
}

public unsafe struct DescriptorSetLayout : IDisposable {
    public readonly LogicalDevice Device;

    internal VkDescriptorSetLayout Value;

    public DescriptorSetLayout(LogicalDevice logicalDevice, ReadOnlySpan<DescriptorSetLayoutBinding> bindings) {
        Device = logicalDevice;

        VkDescriptorSetLayoutCreateInfo createInfo = new() {
            bindingCount = (uint)bindings.Length,
            pBindings = (VkDescriptorSetLayoutBinding*)bindings.GetPointer()
        };

        Vulkan.vkCreateDescriptorSetLayout(Device, &createInfo, null, out Value);
    }
    
    public DescriptorSetLayout(LogicalDevice logicalDevice, uint binding, VkDescriptorType type, VkShaderStageFlags stageFlags) {
        Device = logicalDevice;

        VkDescriptorSetLayoutBinding layoutBinding = new() {
            binding = binding,
            descriptorType = type,
            descriptorCount = 1,
            stageFlags = stageFlags
        };

        VkDescriptorSetLayoutCreateInfo createInfo = new() {
            bindingCount = 1,
            pBindings = &layoutBinding  
        };

        Vulkan.vkCreateDescriptorSetLayout(Device, &createInfo, null, out Value);
    }
    
    public DescriptorSetLayout(LogicalDevice logicalDevice, DescriptorSetLayoutBinding binding) {
        Device = logicalDevice;
        Value = VkDescriptorSetLayout.Null;
        
        VkDescriptorSetLayoutBinding vkBinding = new() {
            binding = binding.Binding,
            descriptorType = binding.DescriptorType,
            descriptorCount = binding.DescriptorCount,
            stageFlags = binding.ShaderFlags,
            pImmutableSamplers = binding.ImmutableSamplers
        };

        VkDescriptorSetLayoutCreateInfo createInfo = new() {
            sType = VkStructureType.DescriptorSetLayoutCreateInfo,
            bindingCount = 1,
            pBindings = &vkBinding
        };

        Vulkan.vkCreateDescriptorSetLayout(Device, &createInfo, null, out Value);
    }
    
    public void Dispose() {
        if (Value != VkDescriptorSetLayout.Null) {
            Vulkan.vkDestroyDescriptorSetLayout(Device, Value, null);
        }
    }

    public static implicit operator VkDescriptorSetLayout(DescriptorSetLayout layout) => layout.Value;
}