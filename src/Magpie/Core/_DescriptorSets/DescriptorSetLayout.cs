using Magpie.Utilities;
using Vortice.Vulkan;

namespace Magpie.Core;

public unsafe struct DescriptorSetLayoutBinding {
    public uint Binding;
    public VkDescriptorType DescriptorType;
    public uint DescriptorCount;
    public VkShaderStageFlags ShaderFlags;
    public VkDescriptorBindingFlags BindingFlags;
    internal VkSampler* ImmutableSamplers;

    public DescriptorSetLayoutBinding(uint binding, VkDescriptorType descriptorType, uint descriptorCount, VkShaderStageFlags shaderFlags, VkDescriptorBindingFlags bindingFlags = VkDescriptorBindingFlags.None) {
        Binding = binding;
        DescriptorType = descriptorType;
        DescriptorCount = descriptorCount;
        ShaderFlags = shaderFlags;
        BindingFlags = bindingFlags;
        ImmutableSamplers = default;
    }
}

public unsafe struct DescriptorSetLayout : IDisposable {
    public readonly LogicalDevice Device;

    internal VkDescriptorSetLayout Value;

    public DescriptorSetLayout(LogicalDevice logicalDevice, ReadOnlySpan<DescriptorSetLayoutBinding> bindings) {
        Device = logicalDevice;

        VkDescriptorSetLayoutCreateInfo createInfo = new() {
            bindingCount = (uint)bindings.Length
        };

        if (bindings.Length > 0) {
            VkDescriptorSetLayoutBinding* vkBindings = stackalloc VkDescriptorSetLayoutBinding[bindings.Length];
            VkDescriptorBindingFlags* vkBindingFlags = stackalloc VkDescriptorBindingFlags[bindings.Length];
            bool hasBindingFlags = false;

            for (int i = 0; i < bindings.Length; i++) {
                DescriptorSetLayoutBinding binding = bindings[i];
                vkBindings[i] = new VkDescriptorSetLayoutBinding {
                    binding = binding.Binding,
                    descriptorType = binding.DescriptorType,
                    descriptorCount = binding.DescriptorCount,
                    stageFlags = binding.ShaderFlags,
                    pImmutableSamplers = binding.ImmutableSamplers
                };
                vkBindingFlags[i] = binding.BindingFlags;
                hasBindingFlags |= binding.BindingFlags != VkDescriptorBindingFlags.None;
            }

            createInfo.pBindings = vkBindings;

            if (hasBindingFlags) {
                VkDescriptorSetLayoutBindingFlagsCreateInfo bindingFlagsInfo = new() {
                    sType = VkStructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
                    bindingCount = (uint)bindings.Length,
                    pBindingFlags = vkBindingFlags
                };

                createInfo.flags |= VkDescriptorSetLayoutCreateFlags.UpdateAfterBindPool;
                createInfo.pNext = &bindingFlagsInfo;

                Vulkan.vkCreateDescriptorSetLayout(Device, &createInfo, null, out Value);
                return;
            }
        }

        Vulkan.vkCreateDescriptorSetLayout(Device, &createInfo, null, out Value);
    }
    
    public DescriptorSetLayout(LogicalDevice logicalDevice, uint binding, VkDescriptorType type, VkShaderStageFlags stageFlags, VkDescriptorBindingFlags bindingFlags = VkDescriptorBindingFlags.None) {
        Device = logicalDevice;

        VkDescriptorSetLayoutBinding layoutBinding = new() {
            binding = binding,
            descriptorType = type,
            descriptorCount = 1,
            stageFlags = stageFlags
        };

        VkDescriptorBindingFlags bindingFlagsValue = bindingFlags;
        VkDescriptorSetLayoutBindingFlagsCreateInfo bindingFlagsInfo = new() {
            sType = VkStructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            bindingCount = 1,
            pBindingFlags = &bindingFlagsValue
        };

        VkDescriptorSetLayoutCreateInfo createInfo = new() {
            pNext = &bindingFlagsInfo,
            bindingCount = 1,
            pBindings = &layoutBinding,
            flags = bindingFlags != VkDescriptorBindingFlags.None ? VkDescriptorSetLayoutCreateFlags.UpdateAfterBindPool : 0
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

        VkDescriptorBindingFlags bindingFlagsValue = binding.BindingFlags;
        VkDescriptorSetLayoutBindingFlagsCreateInfo bindingFlagsInfo = new() {
            sType = VkStructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            bindingCount = 1,
            pBindingFlags = &bindingFlagsValue
        };

        VkDescriptorSetLayoutCreateInfo createInfo = new() {
            sType = VkStructureType.DescriptorSetLayoutCreateInfo,
            bindingCount = 1,
            pBindings = &vkBinding,
            pNext = &bindingFlagsInfo,
            flags = binding.BindingFlags != VkDescriptorBindingFlags.None ? VkDescriptorSetLayoutCreateFlags.UpdateAfterBindPool : 0
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