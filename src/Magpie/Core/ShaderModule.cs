using Magpie.Core;
using Vortice.Vulkan;

namespace Magpie.Graphics;

public unsafe struct ShaderModule : IDisposable {
    internal VkShaderModule Value;
    public readonly bool IsDisposed => Value.IsNull;

    public readonly LogicalDevice Device;

    public ShaderModule(LogicalDevice logicalDevice, byte[] code) {
        Device = logicalDevice;
        var result = Vulkan.vkCreateShaderModule(Device, code, null, out Value);
        if(result != VkResult.Success)
            throw new Exception($"failed to create shader module!: {result}");
    }
    
    public void Dispose() {
        Vulkan.vkDestroyShaderModule(Device, Value, null);
    }
    
    public static implicit operator VkShaderModule(ShaderModule module) => module.Value;
}