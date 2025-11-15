using System.Diagnostics;
using System.Numerics;
using Magpie.Core;
using Magpie.Graphics;
using Magpie.Utilities;
using SDL3;
using ShaderCompilation;
using StainedGlass;
using Standard;
using Vortice.Vulkan;

namespace Samples;

internal sealed unsafe class VkSample {
    public bool Quit;

    public GraphicsDevice? Graphics;
    private VkCtx? _vkContext;
    private SdlCtx? _sdlContext;
    private VulkanInstance _vkInstance;
    private Surface _vkSurface;
    private LogicalDevice _vkDevice;
    
    private SdlWindow _windowHandle;
    private ShaderCompiler? _compiler;
    
    private Pipeline _pipeline;
    private VkBuffer _vertexBuffer;
    private VkDeviceMemory _vertexBufferMemory;
    
    public void Initialize(string[] args) { 
        _compiler = new ShaderCompiler();
        
        _vkContext = new("vulkan");
        _sdlContext = new(SDL.InitFlags.Video | SDL.InitFlags.Events);
        _vkInstance = new(_vkContext, "magpieTests", "magpieco");
        
        _windowHandle = new("magpi", 400, 400, SDL.WindowFlags.Vulkan | SDL.WindowFlags.Resizable);
        _vkSurface = new(_vkInstance, _windowHandle.CreateVulkanSurface(_vkInstance));
        
        string[] requiredDeviceExtensions = [
            new VkUtf8String(Vulkan.VK_KHR_SWAPCHAIN_EXTENSION_NAME.GetPointer()).ToString()!,
            new VkUtf8String(Vulkan.VK_EXT_DESCRIPTOR_INDEXING_EXTENSION_NAME.GetPointer()).ToString()!,
            new VkUtf8String(Vulkan.VK_KHR_RAY_TRACING_PIPELINE_EXTENSION_NAME.GetPointer()).ToString()!,
            new VkUtf8String(Vulkan.VK_KHR_ACCELERATION_STRUCTURE_EXTENSION_NAME.GetPointer()).ToString()!,
            new VkUtf8String(Vulkan.VK_KHR_DEFERRED_HOST_OPERATIONS_EXTENSION_NAME.GetPointer()).ToString()!,
            "VK_KHR_spirv_1_4",
            "VK_KHR_dynamic_rendering"
        ];
        
        PhysicalDevice bestDevice;
        if (!_vkInstance.TryGetBestPhysicalDevice(requiredDeviceExtensions, out bestDevice)) {
            throw new InvalidOperationException("no valid physical device found");
        }   
        
        uint graphicsQueueFamilyIndex;
        if (!bestDevice.TryGetGraphicsQueueFamily(out graphicsQueueFamilyIndex)) {
            throw new Exception("selected physical device does not have a graphics queue family");
        }

        _vkDevice = new(bestDevice, [graphicsQueueFamilyIndex], requiredDeviceExtensions);
        Console.WriteLine("selected physical device info:" + bestDevice.ToString());
        
        Graphics = new (_vkInstance, _vkSurface, bestDevice, _vkDevice);
        
        VkUtf8ReadOnlyString entryPoint = "main"u8;
        
        var vertShaderCode = _compiler.CompileShader(@"resources/base/base.vert", ShaderKind.Vertex, true);
        var fragShaderCode = _compiler.CompileShader(@"resources/base/base.frag", ShaderKind.Fragment, true);

        var vertmodule = new ShaderModule(_vkDevice, vertShaderCode.ToArray());
        var fragmodule = new ShaderModule(_vkDevice, fragShaderCode.ToArray());
        
        
        VkVertexInputBindingDescription vertexInputBinding = new(VertexPositionColor.SizeInBytes);

        // Attribute location 0: Position
        // Attribute location 1: Color
        ReadOnlySpan<VkVertexInputAttributeDescription> vertexInputAttributs = stackalloc VkVertexInputAttributeDescription[2]
        {
            new(0, VkFormat.R32G32B32Sfloat, 0),
            new(1, VkFormat.R32G32B32A32Sfloat, 12)
        };
        
        _pipeline = new Pipeline(
            _vkDevice,
            Graphics.MainSwapchain.Format,
            vertShaderCode.ToArray(),
            fragShaderCode.ToArray(),
            vertexInputBinding,
            vertexInputAttributs
        );

        Vulkan.vkDestroyShaderModule(_vkDevice, vertmodule);
        Vulkan.vkDestroyShaderModule(_vkDevice, fragmodule);
        
        ReadOnlySpan<VertexPositionColor> sourceData =
        [
            new VertexPositionColor(new Vector3(0f, 0.5f, 0.0f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f)),
            new VertexPositionColor(new Vector3(0.5f, -0.5f, 0.0f), new Vector4(0.0f, 1.0f, 0.0f, 1.0f)),
            new VertexPositionColor(new Vector3(-0.5f, -0.5f, 0.0f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f))
        ];
        
        uint vertexBufferSize = (uint)(sourceData.Length * VertexPositionColor.SizeInBytes);

        VkBufferCreateInfo vertexBufferInfo = new()
        {
            size = vertexBufferSize,
            // Buffer is used as the copy source
            usage = VkBufferUsageFlags.TransferSrc
        };
        Vulkan.vkCreateBuffer(_vkDevice, &vertexBufferInfo, null, out VkBuffer stagingBuffer).CheckResult();

        Vulkan.vkGetBufferMemoryRequirements(_vkDevice, stagingBuffer, out VkMemoryRequirements memReqs);

        VkMemoryAllocateInfo memAlloc = new()
        {
            allocationSize = memReqs.size,
            // Request a host visible memory type that can be used to copy our data do
            // Also request it to be coherent, so that writes are visible to the GPU right after unmapping the buffer
            memoryTypeIndex = Graphics.GetMemoryTypeIndex(memReqs.memoryTypeBits, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent)
        };
        Vulkan.vkAllocateMemory(_vkDevice, &memAlloc, null, out VkDeviceMemory stagingBufferMemory);
        
        void* pMappedData;
        Vulkan.vkMapMemory(_vkDevice, stagingBufferMemory, 0, memAlloc.allocationSize, 0, &pMappedData).CheckResult();
        Span<VertexPositionColor> destinationData = new(pMappedData, sourceData.Length);
        sourceData.CopyTo(destinationData);
        Vulkan.vkUnmapMemory(_vkDevice, stagingBufferMemory);
        Vulkan.vkBindBufferMemory(_vkDevice, stagingBuffer, stagingBufferMemory, 0).CheckResult();

        vertexBufferInfo.usage = VkBufferUsageFlags.VertexBuffer | VkBufferUsageFlags.TransferDst;
        Vulkan.vkCreateBuffer(_vkDevice, &vertexBufferInfo, null, out _vertexBuffer).CheckResult();

        Vulkan.vkGetBufferMemoryRequirements(_vkDevice, _vertexBuffer, out memReqs);
        memAlloc.allocationSize = memReqs.size;
        memAlloc.memoryTypeIndex = Graphics.GetMemoryTypeIndex(memReqs.memoryTypeBits, VkMemoryPropertyFlags.DeviceLocal);
        Vulkan.vkAllocateMemory(_vkDevice, &memAlloc, null, out _vertexBufferMemory).CheckResult();
        Vulkan.vkBindBufferMemory(_vkDevice, _vertexBuffer, _vertexBufferMemory, 0).CheckResult();
        
        using( var fence = new Fence(_vkDevice, VkFenceCreateFlags.None))
        {
            var copyCmd = Graphics.AllocateCommandBuffer();

            copyCmd.Begin();
            
            VkBufferCopy copyRegion = new() { dstOffset = 0, srcOffset = 0, size = vertexBufferSize};
            Vulkan.vkCmdCopyBuffer(copyCmd, stagingBuffer, _vertexBuffer, 1, &copyRegion);

            copyCmd.End();

            Graphics.Submit(copyCmd, fence);
            fence.Wait();
        }

        Vulkan.vkDestroyBuffer(_vkDevice, stagingBuffer, null);
        Vulkan.vkFreeMemory(_vkDevice, stagingBufferMemory, null);
        
        while (!Quit) {
            Time.Start();
            while (SDL.PollEvent(out var @event)) {
                switch (@event.Type) {
                    case (uint) SDL.EventType.Quit:
                        Quit = true;
                        break;
                    case (uint) SDL.EventType.KeyDown:
                        if (@event.Key.Key == SDL.Keycode.Escape)
                            Quit = true;
                        break;
                }
            }
        
            Time.Update();
            
            Draw();
            
            Time.Stop();
        }
        
        Dispose();
    }

    void Draw() {
        Debug.Assert(Graphics != null);
        Debug.Assert(Graphics != null);

        float time = Time.GlobalTime;
        float r = (float)(Math.Sin(time * 0.5f) * 0.5f + 0.5f);
        float g = (float)(Math.Sin(time * 0.5f + 2) * 0.5f + 0.5f);
        float b = (float)(Math.Sin(time * 0.5f + 4) * 0.5f + 0.5f);
        var color = new Color(r, g, b);
        
        Graphics.Clear(Colors.Transparent);
        
        if (!Graphics.IsFrameStarted)
        {
            return;
        }

        var cmd = Graphics.GetCurrentCommandBuffer();

        Vulkan.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _pipeline);

        var extent = new VkExtent2D(Graphics.MainSwapchain.Width, Graphics.MainSwapchain.Height);
        VkViewport viewport = new(0, 0, extent.width, extent.height, 0.0f, 1.0f);
        Vulkan.vkCmdSetViewport(cmd, 0, 1, &viewport);
        VkRect2D scissor = new(0, 0, extent.width, extent.height);
        Vulkan.vkCmdSetScissor(cmd, 0, 1, &scissor);

        Vulkan.vkCmdBindVertexBuffer(cmd, 0, _vertexBuffer);
        Vulkan.vkCmdDraw(cmd, 3, 1, 0, 0);

        Graphics.Present();
    }

    private void Dispose() {
        Vulkan.vkDeviceWaitIdle(_vkDevice);
        
        // Vulkan.vkDestroyPipelineLayout(_vkDevice, _pipelineLayout);
        // Vulkan.vkDestroyPipeline(_vkDevice, _pipeline);
        _pipeline.Dispose();
        Vulkan.vkDestroyBuffer(_vkDevice, _vertexBuffer);
        Vulkan.vkFreeMemory(_vkDevice, _vertexBufferMemory);
        
        Graphics!.Dispose();
        
        _vkDevice.Dispose();
        _vkInstance.Dispose();
        _vkContext?.Dispose();
        _sdlContext?.Dispose();
        _windowHandle.Dispose();
    }
}