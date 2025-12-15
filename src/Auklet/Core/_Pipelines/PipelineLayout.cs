using Magpie.Utilities;
using Vortice.Vulkan;

namespace Auklet.Core;

public unsafe struct PipelineLayout : IDisposable {
    public LogicalDevice Device;

    internal VkPipelineLayout Value;

    public PipelineLayout(LogicalDevice device) {
        Device = device;
        VkPipelineLayoutCreateInfo pipelineLayoutCreateInfo = new();
        
        Vulkan.vkCreatePipelineLayout(device, &pipelineLayoutCreateInfo, null, out Value)
            .CheckResult("could not create this pipeline layout!");
    }
    
    public PipelineLayout(LogicalDevice device, ReadOnlySpan<DescriptorSetLayout> setLayouts, ReadOnlySpan<PushConstant> pushConstants) {
        Device = device;

        VkPipelineLayoutCreateInfo pipelineLayoutCreateInfo = new();
        if (setLayouts.Length > 0) {
            Span<VkDescriptorSetLayout> layouts = stackalloc VkDescriptorSetLayout[setLayouts.Length];
            for (int i = 0; i < setLayouts.Length; i++) {
                layouts[i] = setLayouts[i].Value;
            }

            pipelineLayoutCreateInfo.pSetLayouts = layouts.GetPointer();
            pipelineLayoutCreateInfo.setLayoutCount = (uint)setLayouts.Length;
        }

        if (pushConstants.Length > 0) {
            Span<VkPushConstantRange> constants = stackalloc VkPushConstantRange[pushConstants.Length];
            for (int i = 0; i < pushConstants.Length; i++)
            {
                PushConstant constant = pushConstants[i];
                constants[i] = new()
                {
                    offset = constant.Offset,
                    size = constant.Size,
                    stageFlags = constant.Stage
                };
            }

            pipelineLayoutCreateInfo.pPushConstantRanges = constants.GetPointer();
            pipelineLayoutCreateInfo.pushConstantRangeCount = (uint)pushConstants.Length;
        }

        Vulkan.vkCreatePipelineLayout(device, &pipelineLayoutCreateInfo, null, out Value)
            .CheckResult("could not create this pipeline layout!");
    }

    public void Dispose() {
        Vulkan.vkDestroyPipelineLayout(Device, Value);
        Value = default;
    }
    
    public static implicit operator VkPipelineLayout(PipelineLayout layout) => layout.Value;
}

public readonly struct PushConstant(uint offset, uint size, VkShaderStageFlags stage) {
    public readonly uint Offset = offset;
    public readonly uint Size = size;
    public readonly VkShaderStageFlags Stage = stage;
}