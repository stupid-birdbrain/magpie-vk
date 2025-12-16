using Auklet.Core;
using Vortice.Vulkan;

namespace Auklet;

public unsafe struct Sampler : IDisposable {
    public LogicalDevice Device;

    internal VkSampler Value;
    
    public Sampler(LogicalDevice logicalDevice, SamplerCreateParameters createInfo) {
        Device = logicalDevice;
        Value = VkSampler.Null;
        
        VkSamplerCreateInfo samplerCreateInfo = new()
        {
            sType =  VkStructureType.SamplerCreateInfo,
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
    }

    public void Dispose() {
        if (Value != VkSampler.Null) {
            Vulkan.vkDestroySampler(Device, Value, null);
            Value = VkSampler.Null;
        }
    }
}

public struct SamplerCreateParameters(VkFilter filter, VkSamplerAddressMode addressMode) {
    public VkFilter MagFilter = filter;
    public VkFilter MinFilter = filter;
    public VkSamplerMipmapMode MipmapMode = filter == VkFilter.Nearest ? VkSamplerMipmapMode.Nearest : VkSamplerMipmapMode.Linear;
    public bool Anisotropy = true;
    public float MaxAnisotropy = 16.0f;
    public VkSamplerAddressMode AddressModeX = addressMode;
    public VkSamplerAddressMode AddressModeY = addressMode;
    public VkSamplerAddressMode AddressModeW = addressMode;
    public float MipLoadBias = 0.0f;
    public float MinLod = 0.0f;
    public float MaxLod = 0.0f;
    public VkCompareOp CompareOperation = VkCompareOp.Always;
    public bool CompareEnable;
    
    public SamplerCreateParameters() : this(VkFilter.Linear, VkSamplerAddressMode.Repeat) { }
}