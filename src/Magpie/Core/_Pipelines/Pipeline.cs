using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie.Core;

public readonly unsafe struct Pipeline : IDisposable {
    public readonly LogicalDevice Device;
    public readonly VkPipeline Value;
    public readonly PipelineLayout Layout;

    public Pipeline(
        LogicalDevice device,
        VkFormat swapchainFormat,
        VkFormat depthFormat,
        PipelineCreationDescription description,
        PipelineLayout pipelineLayout,
        VkVertexInputBindingDescription vertexBinding,
        ReadOnlySpan<VkVertexInputAttributeDescription> vertexAttributes,
        VkUtf8ReadOnlyString entryPoint = default
    )
    {
        Device = device;
        Layout = pipelineLayout;
        Value = VkPipeline.Null;

        VkPipelineShaderStageCreateInfo* shaderStages = stackalloc VkPipelineShaderStageCreateInfo[2];
        shaderStages[0] = new() {
            sType = VkStructureType.PipelineShaderStageCreateInfo,
            stage = VkShaderStageFlags.Vertex,
            module = description.VertexShader,
            pName = entryPoint
        };
        shaderStages[1] = new() {
            sType = VkStructureType.PipelineShaderStageCreateInfo,
            stage = VkShaderStageFlags.Fragment,
            module = description.FragmentShader,
            pName = entryPoint
        };

        fixed (VkVertexInputAttributeDescription* pVertexAttributes = vertexAttributes) {
            VkPipelineVertexInputStateCreateInfo vertexInputState = new() {
                sType = VkStructureType.PipelineVertexInputStateCreateInfo,
                vertexBindingDescriptionCount = 1,
                pVertexBindingDescriptions = &vertexBinding,
                vertexAttributeDescriptionCount = (uint)vertexAttributes.Length,
                pVertexAttributeDescriptions = pVertexAttributes
            };

            VkPipelineInputAssemblyStateCreateInfo inputAssemblyState = new() {
                sType = VkStructureType.PipelineInputAssemblyStateCreateInfo,
                topology = description.PrimitiveTopology,
                primitiveRestartEnable = description.PrimitiveRestartEnable
            };
            VkPipelineViewportStateCreateInfo viewportState = new() { sType = VkStructureType.PipelineViewportStateCreateInfo, viewportCount = 1, scissorCount = 1 };

            VkPipelineRasterizationStateCreateInfo rasterizationState = new() {
                sType = VkStructureType.PipelineRasterizationStateCreateInfo,
                polygonMode = description.PolygonMode,
                lineWidth = 1.0f,
                cullMode = description.CullMode,
                frontFace = description.FrontFace
            };
            
            VkPipelineMultisampleStateCreateInfo multisampleState = new() {
                sType = VkStructureType.PipelineMultisampleStateCreateInfo, 
                rasterizationSamples = VkSampleCountFlags.Count1,
                alphaToCoverageEnable = true
            };

            VkPipelineDepthStencilStateCreateInfo depthStencilState = new() {
                sType = VkStructureType.PipelineDepthStencilStateCreateInfo,
                depthTestEnable = description.DepthTestEnable,
                depthWriteEnable = description.DepthWriteEnable,
                depthCompareOp = description.DepthCompareOp,
                stencilTestEnable = description.StencilTestEnable,
                
                depthBoundsTestEnable = false,
                minDepthBounds = 0.0f,
                maxDepthBounds = 1.0f,
            };

            VkPipelineColorBlendAttachmentState blendAttachmentState = new() {
                colorWriteMask = VkColorComponentFlags.All,
                blendEnable = description.BlendSettings.BlendEnable,
                srcColorBlendFactor = (VkBlendFactor)description.BlendSettings.SourceColorBlend,
                dstColorBlendFactor = (VkBlendFactor)description.BlendSettings.DestinationColorBlend,
                colorBlendOp = (VkBlendOp)description.BlendSettings.ColorBlendOperation,
                srcAlphaBlendFactor = (VkBlendFactor)description.BlendSettings.SourceAlphaBlend,
                dstAlphaBlendFactor = (VkBlendFactor)description.BlendSettings.DestinationAlphaBlend,
                alphaBlendOp = (VkBlendOp)description.BlendSettings.AlphaBlendOperation
            };
            VkPipelineColorBlendStateCreateInfo colorBlendState = new() { sType = VkStructureType.PipelineColorBlendStateCreateInfo, attachmentCount = 1, pAttachments = &blendAttachmentState };
            VkDynamicState* dynamicStateEnables = stackalloc VkDynamicState[] { VkDynamicState.Viewport, VkDynamicState.Scissor };
            VkPipelineDynamicStateCreateInfo dynamicState = new() { sType = VkStructureType.PipelineDynamicStateCreateInfo, dynamicStateCount = 2, pDynamicStates = dynamicStateEnables };

            VkPipelineRenderingCreateInfo renderingInfo = new() {
                sType = VkStructureType.PipelineRenderingCreateInfo,
                colorAttachmentCount = 1,
                pColorAttachmentFormats = &swapchainFormat,
                depthAttachmentFormat = depthFormat,
                stencilAttachmentFormat = VkFormat.Undefined
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
                layout = Layout.Value,
                renderPass = VkRenderPass.Null
            };

            fixed(VkPipeline* ptr = &Value)
                vkCreateGraphicsPipelines(device, VkPipelineCache.Null, 1, &pipelineCreateInfo, null, ptr)
                    .CheckResult();
        }
    }
    
        public Pipeline(
        LogicalDevice device,
        VkFormat swapchainFormat,
        VkFormat depthFormat,
        PipelineCreationDescription description,
        PipelineLayout pipelineLayout, // This takes your PipelineLayout struct instance
        ReadOnlySpan<VkVertexInputBindingDescription> vertexBindings, // Now takes a span of bindings
        ReadOnlySpan<VkVertexInputAttributeDescription> vertexAttributes, // Now takes a span of attributes
        VkUtf8ReadOnlyString entryPoint = default // Default to "main" if not provided
    )
    {
        Device = device;
        Layout = pipelineLayout; // Assign the VkPipelineLayout handle from the provided PipelineLayout struct
        Value = VkPipeline.Null; // Initialize to null; set later in fixed block

        // Shader Stages (using ShaderModules from description)
        VkPipelineShaderStageCreateInfo* shaderStages = stackalloc VkPipelineShaderStageCreateInfo[2];
        shaderStages[0] = new() {
            sType = VkStructureType.PipelineShaderStageCreateInfo,
            stage = VkShaderStageFlags.Vertex,
            module = description.VertexShader,
            pName = entryPoint
        };
        shaderStages[1] = new() {
            sType = VkStructureType.PipelineShaderStageCreateInfo,
            stage = VkShaderStageFlags.Fragment,
            module = description.FragmentShader,
            pName = entryPoint
        };

        // Pin pointers to spans for Vulkan API calls
        fixed (VkVertexInputAttributeDescription* pVertexAttributes = vertexAttributes)
        fixed (VkVertexInputBindingDescription* pVertexBindings = vertexBindings)
        {
            VkPipelineVertexInputStateCreateInfo vertexInputState = new()
            {
                sType = VkStructureType.PipelineVertexInputStateCreateInfo,
                vertexBindingDescriptionCount = (uint)vertexBindings.Length, // Use length of binding span
                pVertexBindingDescriptions = pVertexBindings, // Use pointer to binding span
                vertexAttributeDescriptionCount = (uint)vertexAttributes.Length,
                pVertexAttributeDescriptions = pVertexAttributes
            };

            VkPipelineInputAssemblyStateCreateInfo inputAssemblyState = new() {
                sType = VkStructureType.PipelineInputAssemblyStateCreateInfo,
                topology = description.PrimitiveTopology,
                primitiveRestartEnable = description.PrimitiveRestartEnable
            };
            VkPipelineViewportStateCreateInfo viewportState = new() { sType = VkStructureType.PipelineViewportStateCreateInfo, viewportCount = 1, scissorCount = 1 };

            VkPipelineRasterizationStateCreateInfo rasterizationState = new()
            {
                sType = VkStructureType.PipelineRasterizationStateCreateInfo,
                polygonMode = description.PolygonMode,
                lineWidth = 1.0f,
                cullMode = description.CullMode,
                frontFace = description.FrontFace
            };
            VkPipelineMultisampleStateCreateInfo multisampleState = new()
            {
                sType = VkStructureType.PipelineMultisampleStateCreateInfo, rasterizationSamples = VkSampleCountFlags.Count1
            };

            // Depth/Stencil State configuration from description
            VkPipelineDepthStencilStateCreateInfo depthStencilState = new()
            {
                sType = VkStructureType.PipelineDepthStencilStateCreateInfo,
                depthTestEnable = description.DepthTestEnable,
                depthWriteEnable = description.DepthWriteEnable,
                depthCompareOp = description.DepthCompareOp,
                stencilTestEnable = description.StencilTestEnable,
                depthBoundsTestEnable = false, // Not exposed in PipelineCreationDescription, default false
                minDepthBounds = 0.0f,
                maxDepthBounds = 1.0f,
            };

            // Blending State configuration from description
            VkPipelineColorBlendAttachmentState blendAttachmentState = new() {
                colorWriteMask = VkColorComponentFlags.All, // Assuming all color channels are writable
                blendEnable = description.BlendSettings.BlendEnable,
                srcColorBlendFactor = (VkBlendFactor)description.BlendSettings.SourceColorBlend,
                dstColorBlendFactor = (VkBlendFactor)description.BlendSettings.DestinationColorBlend,
                colorBlendOp = (VkBlendOp)description.BlendSettings.ColorBlendOperation,
                srcAlphaBlendFactor = (VkBlendFactor)description.BlendSettings.SourceAlphaBlend,
                dstAlphaBlendFactor = (VkBlendFactor)description.BlendSettings.DestinationAlphaBlend,
                alphaBlendOp = (VkBlendOp)description.BlendSettings.AlphaBlendOperation
            };
            VkPipelineColorBlendStateCreateInfo colorBlendState = new() { sType = VkStructureType.PipelineColorBlendStateCreateInfo, attachmentCount = 1, pAttachments = &blendAttachmentState };
            VkDynamicState* dynamicStateEnables = stackalloc VkDynamicState[] { VkDynamicState.Viewport, VkDynamicState.Scissor };
            VkPipelineDynamicStateCreateInfo dynamicState = new() { sType = VkStructureType.PipelineDynamicStateCreateInfo, dynamicStateCount = 2, pDynamicStates = dynamicStateEnables };

            VkPipelineRenderingCreateInfo renderingInfo = new()
            {
                sType = VkStructureType.PipelineRenderingCreateInfo,
                colorAttachmentCount = 1,
                pColorAttachmentFormats = &swapchainFormat,
                depthAttachmentFormat = depthFormat,
                stencilAttachmentFormat = VkFormat.Undefined // Assuming no stencil attachment
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
                layout = Layout, // Use the provided VkPipelineLayout handle
                renderPass = VkRenderPass.Null // Explicitly null for dynamic rendering
            };

            fixed(VkPipeline* ptr = &Value)
                vkCreateGraphicsPipelines(device, VkPipelineCache.Null, 1, &pipelineCreateInfo, null, ptr).CheckResult();
        }
    }
    
    public Pipeline(
        LogicalDevice device,
        ReadOnlySpan<byte> computeShaderCode,
        DescriptorSetLayout descriptorSetLayout,
        VkPushConstantRange? pushConstantRange = null)
    {
        Device = device;
        VkDescriptorSetLayout descLayout = descriptorSetLayout.Value;
        VkPipelineLayoutCreateInfo pipelineLayoutCreateInfo = new() {
            sType = VkStructureType.PipelineLayoutCreateInfo,
            setLayoutCount = 1,
            pSetLayouts = &descLayout
        };

        if (pushConstantRange.HasValue) {
            VkPushConstantRange pcr = pushConstantRange.Value;
            pipelineLayoutCreateInfo.pushConstantRangeCount = 1;
            pipelineLayoutCreateInfo.pPushConstantRanges = &pcr;
        }

        vkCreatePipelineLayout(device, &pipelineLayoutCreateInfo, null, out Layout.Value).CheckResult();
        Value = VkPipeline.Null;
        
        using var computeModule = new ShaderModule(device, computeShaderCode.ToArray());

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
            layout = Layout.Value
        };

        fixed (VkPipeline* ptr = &Value)
        {
            vkCreateComputePipelines(device, VkPipelineCache.Null, 1, &pipelineCreateInfo, null, ptr).CheckResult();
        }
    }

    public void Dispose() {
        if (Value != VkPipeline.Null) {
            vkDestroyPipeline(Device, Value, null);
        }
    }

    public static implicit operator VkPipeline(Pipeline p) => p.Value;
}