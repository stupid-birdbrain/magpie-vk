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
        private VkBuffer _indexBuffer;
        private VkDeviceMemory _indexBufferMemory;
        
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
            
        ReadOnlySpan<VertexPositionColor> sourceVertexData =
        [
            // Top-Left
            new VertexPositionColor(new Vector3(-0.5f, -0.5f, 0.0f), new Vector4(1.0f, 0.0f, 0.5f, 1.0f)),
            // Top-Right
            new VertexPositionColor(new Vector3(0.5f, -0.5f, 0.0f), new Vector4(0.0f, 1.0f, 0.3f, 1.0f)),
            // Bottom-Right
            new VertexPositionColor(new Vector3(0.5f, 0.5f, 0.0f), new Vector4(0.0f, 0.3f, 1.0f, 1.0f)),
            // Bottom-Left
            new VertexPositionColor(new Vector3(-0.5f, 0.5f, 0.0f), new Vector4(1.0f, 1.0f, 1.0f, 1.0f))
        ];
        uint vertexBufferSize = (uint)(sourceVertexData.Length * VertexPositionColor.SizeInBytes);

        // --- Quad Index Data (6 indices for 2 triangles) ---
        // Indices to form two triangles (0,3,2) and (0,2,1) in Counter-Clockwise winding
        ReadOnlySpan<uint> sourceIndexData = [0, 3, 2, 0, 2, 1];
        uint indexBufferSize = (uint)(sourceIndexData.Length * sizeof(uint));

        // Create Staging Buffers (Host-visible)
        VkBufferCreateInfo stagingBufferInfo = new() { sType = VkStructureType.BufferCreateInfo, size = vertexBufferSize, usage = VkBufferUsageFlags.TransferSrc };
        Vulkan.vkCreateBuffer(_vkDevice, &stagingBufferInfo, null, out VkBuffer stagingVertexBuffer).CheckResult();
        VkBufferCreateInfo stagingIndexBufferInfo = new() { sType = VkStructureType.BufferCreateInfo, size = indexBufferSize, usage = VkBufferUsageFlags.TransferSrc };
        Vulkan.vkCreateBuffer(_vkDevice, &stagingIndexBufferInfo, null, out VkBuffer stagingIndexBuffer).CheckResult();

        // Get Memory Requirements and Allocate Host-Visible Memory
        Vulkan.vkGetBufferMemoryRequirements(_vkDevice, stagingVertexBuffer, out VkMemoryRequirements vertexMemReqs);
        Vulkan.vkGetBufferMemoryRequirements(_vkDevice, stagingIndexBuffer, out VkMemoryRequirements indexMemReqs);

        VkMemoryAllocateInfo vertexMemAlloc = new() { sType = VkStructureType.MemoryAllocateInfo, allocationSize = vertexMemReqs.size, memoryTypeIndex = Graphics.GetMemoryTypeIndex(vertexMemReqs.memoryTypeBits, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent) };
        Vulkan.vkAllocateMemory(_vkDevice, &vertexMemAlloc, null, out VkDeviceMemory stagingVertexBufferMemory).CheckResult();
        Vulkan.vkBindBufferMemory(_vkDevice, stagingVertexBuffer, stagingVertexBufferMemory, 0).CheckResult();

        VkMemoryAllocateInfo indexMemAlloc = new() { sType = VkStructureType.MemoryAllocateInfo, allocationSize = indexMemReqs.size, memoryTypeIndex = Graphics.GetMemoryTypeIndex(indexMemReqs.memoryTypeBits, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent) };
        Vulkan.vkAllocateMemory(_vkDevice, &indexMemAlloc, null, out VkDeviceMemory stagingIndexBufferMemory).CheckResult();
        Vulkan.vkBindBufferMemory(_vkDevice, stagingIndexBuffer, stagingIndexBufferMemory, 0).CheckResult();
        
        // Map and Copy Vertex Data
        void* pMappedVertexData;
        Vulkan.vkMapMemory(_vkDevice, stagingVertexBufferMemory, 0, vertexMemAlloc.allocationSize, 0, &pMappedVertexData).CheckResult();
        sourceVertexData.CopyTo(new Span<VertexPositionColor>(pMappedVertexData, sourceVertexData.Length));
        Vulkan.vkUnmapMemory(_vkDevice, stagingVertexBufferMemory);

        // Map and Copy Index Data
        void* pMappedIndexData;
        Vulkan.vkMapMemory(_vkDevice, stagingIndexBufferMemory, 0, indexMemAlloc.allocationSize, 0, &pMappedIndexData).CheckResult();
        sourceIndexData.CopyTo(new Span<uint>(pMappedIndexData, sourceIndexData.Length));
        Vulkan.vkUnmapMemory(_vkDevice, stagingIndexBufferMemory);

        // Create Device-Local Buffers (Vertex and Index)
        VkBufferCreateInfo deviceVertexBufferInfo = new() { sType = VkStructureType.BufferCreateInfo, size = vertexBufferSize, usage = VkBufferUsageFlags.VertexBuffer | VkBufferUsageFlags.TransferDst };
        Vulkan.vkCreateBuffer(_vkDevice, &deviceVertexBufferInfo, null, out _vertexBuffer).CheckResult();
        Vulkan.vkGetBufferMemoryRequirements(_vkDevice, _vertexBuffer, out vertexMemReqs);
        vertexMemAlloc.allocationSize = vertexMemReqs.size;
        vertexMemAlloc.memoryTypeIndex = Graphics.GetMemoryTypeIndex(vertexMemReqs.memoryTypeBits, VkMemoryPropertyFlags.DeviceLocal);
        Vulkan.vkAllocateMemory(_vkDevice, &vertexMemAlloc, null, out _vertexBufferMemory).CheckResult();
        Vulkan.vkBindBufferMemory(_vkDevice, _vertexBuffer, _vertexBufferMemory, 0).CheckResult();

        VkBufferCreateInfo deviceIndexBufferInfo = new() { sType = VkStructureType.BufferCreateInfo, size = indexBufferSize, usage = VkBufferUsageFlags.IndexBuffer | VkBufferUsageFlags.TransferDst };
        Vulkan.vkCreateBuffer(_vkDevice, &deviceIndexBufferInfo, null, out _indexBuffer).CheckResult();
        Vulkan.vkGetBufferMemoryRequirements(_vkDevice, _indexBuffer, out indexMemReqs);
        indexMemAlloc.allocationSize = indexMemReqs.size;
        indexMemAlloc.memoryTypeIndex = Graphics.GetMemoryTypeIndex(indexMemReqs.memoryTypeBits, VkMemoryPropertyFlags.DeviceLocal);
        Vulkan.vkAllocateMemory(_vkDevice, &indexMemAlloc, null, out _indexBufferMemory).CheckResult();
        Vulkan.vkBindBufferMemory(_vkDevice, _indexBuffer, _indexBufferMemory, 0).CheckResult();
        
        // Copy from staging to device-local buffers using a command buffer
        using(var fence = Graphics.RequestFence(VkFenceCreateFlags.None)) {
            var copyCmd = Graphics.AllocateCommandBuffer(true);

            copyCmd.Begin(VkCommandBufferUsageFlags.OneTimeSubmit);
            
            VkBufferCopy vertexCopyRegion = new() { dstOffset = 0, srcOffset = 0, size = vertexBufferSize};
            Vulkan.vkCmdCopyBuffer(copyCmd, stagingVertexBuffer, _vertexBuffer, 1, &vertexCopyRegion);

            VkBufferCopy indexCopyRegion = new() { dstOffset = 0, srcOffset = 0, size = indexBufferSize};
            Vulkan.vkCmdCopyBuffer(copyCmd, stagingIndexBuffer, _indexBuffer, 1, &indexCopyRegion);

            copyCmd.End();

            Graphics.Submit(copyCmd, fence);
            fence.Wait(); // Wait for the copy to complete
        }

        // Destroy staging resources
        Vulkan.vkDestroyBuffer(_vkDevice, stagingVertexBuffer, null);
        Vulkan.vkFreeMemory(_vkDevice, stagingVertexBufferMemory, null);
        Vulkan.vkDestroyBuffer(_vkDevice, stagingIndexBuffer, null); // NEW
        Vulkan.vkFreeMemory(_vkDevice, stagingIndexBufferMemory, null); // NEW
            
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

            float time = Time.GlobalTime;
            float r = (float)(Math.Sin(time * 0.5f) * 0.5f + 0.5f);
            float g = (float)(Math.Sin(time * 0.5f + 2) * 0.5f + 0.5f);
            float b = (float)(Math.Sin(time * 0.5f + 4) * 0.5f + 0.5f);
            var color = new Color(r, g, b);

            if(!Graphics.Begin(color)) return;
            
            var cmd = Graphics.RequestCurrentCommandBuffer();
            
            Vulkan.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _pipeline);
            
            var extent = new Vector2Int((int)Graphics.MainSwapchain.Width, (int)Graphics.MainSwapchain.Height);
            cmd.SetViewport(new(0, 0, extent.X, extent.Y));
            cmd.SetScissor(new(0, 0, (uint)extent.X, (uint)extent.Y));
            
            Vulkan.vkCmdBindVertexBuffer(cmd, 0, _vertexBuffer);
            Vulkan.vkCmdBindIndexBuffer(cmd, _indexBuffer, 0, VkIndexType.Uint32);
            Vulkan.vkCmdDrawIndexed(cmd, 6, 1, 0, 0, 0);

            Graphics.End();
        }

        private void Dispose() {
            Vulkan.vkDeviceWaitIdle(_vkDevice);
            
            _pipeline.Dispose();
            Vulkan.vkDestroyBuffer(_vkDevice, _vertexBuffer);
            Vulkan.vkFreeMemory(_vkDevice, _vertexBufferMemory);
            Vulkan.vkDestroyBuffer(_vkDevice, _indexBuffer, null);
            Vulkan.vkFreeMemory(_vkDevice, _indexBufferMemory, null);
            
            Graphics!.Dispose();
            
            _vkDevice.Dispose();
            _vkInstance.Dispose();
            _vkContext?.Dispose();
            _sdlContext?.Dispose();
            _windowHandle.Dispose();
        }
    }