using Magpie.Core;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie;

public readonly unsafe struct ComputePipeline : IDisposable {
    public readonly LogicalDevice Device;
    public readonly VkPipeline Value;
    public readonly VkPipelineLayout Layout;

    public ComputePipeline(LogicalDevice device, DescriptorSetLayout descriptorSetLayout, ReadOnlySpan<byte> computeShaderCode) {
        Device = device;

        using var computeModule = new ShaderModule(device, computeShaderCode.ToArray());

        VkDescriptorSetLayout descLayout = descriptorSetLayout.Value;
        VkPipelineLayoutCreateInfo pipelineLayoutCreateInfo = new() {
            sType = VkStructureType.PipelineLayoutCreateInfo,
            setLayoutCount = 1,
            pSetLayouts = &descLayout
        };
        vkCreatePipelineLayout(device, &pipelineLayoutCreateInfo, null, out Layout).CheckResult();

        VkUtf8ReadOnlyString entryPoint = "main"u8;
        VkPipelineShaderStageCreateInfo shaderStage = new() {
            sType = VkStructureType.PipelineShaderStageCreateInfo,
            stage = VkShaderStageFlags.Compute,
            module = computeModule,
            pName = entryPoint
        };

        VkComputePipelineCreateInfo pipelineCreateInfo = new() {
            sType = VkStructureType.ComputePipelineCreateInfo,
            stage = shaderStage,
            layout = Layout
        };

        fixed (VkPipeline* ptr = &Value) {
            vkCreateComputePipelines(device, VkPipelineCache.Null, 1, &pipelineCreateInfo, null, ptr)
                .CheckResult();
        }
    }

    public void Dispose() {
        if (Value != VkPipeline.Null) {
            vkDestroyPipeline(Device, Value, null);
        }
        if (Layout != VkPipelineLayout.Null) {
            vkDestroyPipelineLayout(Device, Layout, null);
        }
    }
}