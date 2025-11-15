using Magpie.Core;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie.Graphics;

public readonly unsafe struct Pipeline : IDisposable {
    public readonly LogicalDevice Device;
    public readonly VkPipeline Value;
    public readonly VkPipelineLayout Layout;

    public Pipeline(
        LogicalDevice device,
        VkFormat swapchainFormat,
        ReadOnlySpan<byte> vertShaderCode,
        ReadOnlySpan<byte> fragShaderCode,
        VkVertexInputBindingDescription vertexBinding,
        ReadOnlySpan<VkVertexInputAttributeDescription> vertexAttributes
    )
    {
        Device = device;

        using var vertModule = new ShaderModule(device, vertShaderCode.ToArray());
        using var fragModule = new ShaderModule(device, fragShaderCode.ToArray());

        VkPipelineLayoutCreateInfo pipelineLayoutCreateInfo = new()
        {
            sType = VkStructureType.PipelineLayoutCreateInfo
        };
        vkCreatePipelineLayout(device, &pipelineLayoutCreateInfo, null, out Layout).CheckResult();

        VkUtf8ReadOnlyString entryPoint = "main"u8;
        VkPipelineShaderStageCreateInfo* shaderStages = stackalloc VkPipelineShaderStageCreateInfo[2];
        shaderStages[0] = new() { sType = VkStructureType.PipelineShaderStageCreateInfo, stage = VkShaderStageFlags.Vertex, module = vertModule, pName = entryPoint };
        shaderStages[1] = new() { sType = VkStructureType.PipelineShaderStageCreateInfo, stage = VkShaderStageFlags.Fragment, module = fragModule, pName = entryPoint };

        fixed (VkVertexInputAttributeDescription* pVertexAttributes = vertexAttributes)
        {
            VkPipelineVertexInputStateCreateInfo vertexInputState = new()
            {
                sType = VkStructureType.PipelineVertexInputStateCreateInfo,
                vertexBindingDescriptionCount = 1,
                pVertexBindingDescriptions = &vertexBinding,
                vertexAttributeDescriptionCount = (uint)vertexAttributes.Length,
                pVertexAttributeDescriptions = pVertexAttributes
            };

            VkPipelineInputAssemblyStateCreateInfo inputAssemblyState = new() { sType = VkStructureType.PipelineInputAssemblyStateCreateInfo, topology = VkPrimitiveTopology.TriangleList };
            VkPipelineViewportStateCreateInfo viewportState = new() { sType = VkStructureType.PipelineViewportStateCreateInfo, viewportCount = 1, scissorCount = 1 };
            VkPipelineRasterizationStateCreateInfo rasterizationState = new() { sType = VkStructureType.PipelineRasterizationStateCreateInfo, polygonMode = VkPolygonMode.Fill, lineWidth = 1.0f, cullMode = VkCullModeFlags.None, frontFace = VkFrontFace.CounterClockwise };
            VkPipelineMultisampleStateCreateInfo multisampleState = new() { sType = VkStructureType.PipelineMultisampleStateCreateInfo, rasterizationSamples = VkSampleCountFlags.Count1 };
            VkPipelineDepthStencilStateCreateInfo depthStencilState = new() { sType = VkStructureType.PipelineDepthStencilStateCreateInfo };
            VkPipelineColorBlendAttachmentState blendAttachmentState = new() { colorWriteMask = VkColorComponentFlags.All, blendEnable = false };
            VkPipelineColorBlendStateCreateInfo colorBlendState = new() { sType = VkStructureType.PipelineColorBlendStateCreateInfo, attachmentCount = 1, pAttachments = &blendAttachmentState };
            VkDynamicState* dynamicStateEnables = stackalloc VkDynamicState[] { VkDynamicState.Viewport, VkDynamicState.Scissor };
            VkPipelineDynamicStateCreateInfo dynamicState = new() { sType = VkStructureType.PipelineDynamicStateCreateInfo, dynamicStateCount = 2, pDynamicStates = dynamicStateEnables };

            VkPipelineRenderingCreateInfo renderingInfo = new()
            {
                sType = VkStructureType.PipelineRenderingCreateInfo,
                colorAttachmentCount = 1,
                pColorAttachmentFormats = &swapchainFormat
            };

            VkGraphicsPipelineCreateInfo pipelineCreateInfo = new()
            {
                sType = VkStructureType.GraphicsPipelineCreateInfo,
                pNext = &renderingInfo,
                stageCount = 2,
                pStages = shaderStages,
                pVertexInputState = &vertexInputState,
                pInputAssemblyState = &inputAssemblyState,
                pViewportState = &viewportState,
                pRasterizationState = &rasterizationState,
                pMultisampleState = &multisampleState,
                pDepthStencilState = &depthStencilState,
                pColorBlendState = &colorBlendState,
                pDynamicState = &dynamicState,
                layout = Layout,
                renderPass = VkRenderPass.Null
            };

            fixed(VkPipeline* ptr = &Value)
                vkCreateGraphicsPipelines(device, VkPipelineCache.Null, 1, &pipelineCreateInfo, null, ptr).CheckResult();
        }
    }

    public void Dispose() {
        vkDestroyPipeline(Device, Value, null);
        vkDestroyPipelineLayout(Device, Layout, null);
    }

    public static implicit operator VkPipeline(Pipeline p) => p.Value;
}