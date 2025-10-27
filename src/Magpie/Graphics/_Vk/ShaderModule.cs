using Vortice.Vulkan;

namespace Magpie.Graphics;

public unsafe struct ShaderModule : IDisposable {
    internal VkShaderModule Value;
    public readonly bool IsDisposed => Value.IsNull;

    public readonly VkDevice LogicalDevice;

    public ShaderModule(VkDevice logicalDevice, byte[] code) {
        LogicalDevice = logicalDevice;
        var result = Vulkan.vkCreateShaderModule(LogicalDevice, code, null, out Value);
        if(result != VkResult.Success)
            throw new Exception($"failed to create shader module!: {result}");
    }
    
    public void Dispose() {
        Vulkan.vkDestroyShaderModule(LogicalDevice, Value, null);
    }
    
    public static implicit operator VkShaderModule(ShaderModule module) => module.Value;
}