using Vortice.Vulkan;

namespace Magpie.Core;

public struct PipelineCreationDescription {
    public ShaderModule VertexShader;
    public ShaderModule FragmentShader;

    public BlendSettings BlendSettings;

    public bool DepthTestEnable;
    public bool DepthWriteEnable;
    public VkCompareOp DepthCompareOp;
    public bool StencilTestEnable;

    public VkCullModeFlags CullMode;
    public VkFrontFace FrontFace;
    public VkPolygonMode PolygonMode;

    public VkPrimitiveTopology PrimitiveTopology;
    public bool PrimitiveRestartEnable;
}