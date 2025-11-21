using Magpie.Core;
using Vortice.Vulkan;

namespace Magpie;

public unsafe struct Sampler {
    public LogicalDevice Device;

    internal VkSampler Value;
    
    public Sampler(LogicalDevice logicalDevice, SamplerCreateParameters createInfo) {
        VkSamplerCreateInfo samplerCreateInfo = new()
        {
            magFilter = createInfo.MagFilter,
            minFilter = createInfo.MinFilter,
            mipmapMode = createInfo.MipmapMode,
            addressModeU = createInfo.AddressModeX,
            addressModeV = createInfo.AddressModeY,
            addressModeW = createInfo.AddressModeW,
            anisotropyEnable = createInfo.Anisotropy
        };

        VkPhysicalDeviceProperties properties = logicalDevice.PhysicalDevice.GetProperties();
        
        {
            samplerCreateInfo.maxAnisotropy = properties.limits.maxSamplerAnisotropy;
            samplerCreateInfo.borderColor = VkBorderColor.IntOpaqueBlack;
            samplerCreateInfo.unnormalizedCoordinates = false;
            samplerCreateInfo.compareEnable = createInfo.CompareEnable;
            samplerCreateInfo.compareOp = createInfo.CompareOperation;
            samplerCreateInfo.mipLodBias = createInfo.MipLoadBias;
            samplerCreateInfo.minLod = createInfo.MinLod;
            samplerCreateInfo.maxLod = createInfo.MaxLod;   
        }

        VkResult result = Vulkan.vkCreateSampler(Device, &samplerCreateInfo, null, out Value);
        Device = logicalDevice;
    }
}

public struct SamplerCreateParameters {
    public VkFilter MagFilter;
    public VkFilter MinFilter;
    public VkSamplerMipmapMode MipmapMode;
    public bool Anisotropy;
    public float MaxAnisotropy;
    public VkSamplerAddressMode AddressModeX;
    public VkSamplerAddressMode AddressModeY;
    public VkSamplerAddressMode AddressModeW;
    public float MipLoadBias;
    public float MinLod;
    public float MaxLod;
    public VkCompareOp CompareOperation;
    public bool CompareEnable;
    
    public SamplerCreateParameters() : this(VkFilter.Linear, VkSamplerAddressMode.Repeat) { }
    
    public SamplerCreateParameters(VkFilter filter, VkSamplerAddressMode addressMode) {
        MinFilter = filter;
        MagFilter = filter;
        MipmapMode = filter == VkFilter.Nearest ? VkSamplerMipmapMode.Nearest : VkSamplerMipmapMode.Linear;
        Anisotropy = true;
        MaxAnisotropy = 16.0f;
        AddressModeX = addressMode;
        AddressModeY = addressMode;
        AddressModeW = addressMode;
        MipLoadBias = 0.0f;
        MinLod = 0.0f;
        MaxLod = 0.0f;
        CompareOperation = VkCompareOp.Always;
    }
}