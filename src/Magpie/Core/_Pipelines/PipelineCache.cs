using Vortice.Vulkan;

namespace Magpie.Core;
    
/// <summary>
///     A Pipeline cache, used to speed up pipeline creation.
/// </summary>
public unsafe struct PipelineCache : IDisposable {
    public readonly LogicalDevice Device;
    internal VkPipelineCache Value;

    public PipelineCache(LogicalDevice device, ReadOnlySpan<byte> initialData = default) {
        Device = device;
        VkPipelineCacheCreateInfo createInfo = new() {
            sType = VkStructureType.PipelineCacheCreateInfo
        };

        fixed (byte* pInitialData = initialData) {
            createInfo.pInitialData = pInitialData;
            createInfo.initialDataSize = (nuint)initialData.Length;
        }
            
        Vulkan.vkCreatePipelineCache(device, &createInfo, null, out Value).CheckResult("Failed to create pipeline cache!");
    }
    
    public byte[] GetCacheData() {
        if (Value == VkPipelineCache.Null) return Array.Empty<byte>();

        nuint dataSize;
        Vulkan.vkGetPipelineCacheData(Device, Value, &dataSize, null).CheckResult("Failed to get pipeline cache data size!");
            
        byte[] data = new byte[(int)dataSize];
        fixed (byte* ptr = data) {
            Vulkan.vkGetPipelineCacheData(Device, Value, &dataSize, ptr).CheckResult("Failed to get pipeline cache data!");
        }
        return data;
    }

    public void Dispose() {
        if (Value != VkPipelineCache.Null) {
            Vulkan.vkDestroyPipelineCache(Device, Value, null);
            Value = VkPipelineCache.Null;
        }
    }

    public static implicit operator VkPipelineCache(PipelineCache cache) => cache.Value;
}