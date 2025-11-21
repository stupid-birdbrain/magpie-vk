using Magpie;
using System.Diagnostics;
using System.Numerics;
using Magpie.Core;
using Magpie.Utilities;
using SDL3;
using ShaderCompilation;
using StainedGlass;
using Standard;
using System.Runtime.InteropServices;
using Vortice.Vulkan;
using Buffer = Magpie.Core.Buffer;

namespace Samples;
    
[StructLayout(LayoutKind.Sequential)]
public struct UniformBufferObject {
    public Matrix4x4 Model;
    public Matrix4x4 View;
    public Matrix4x4 Proj;
}

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
    
    private VertexBuffer<VertexPositionColor> _vertexBuffer;
    private IndexBuffer _indexBuffer;
    
    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _descriptorSet;
    private Buffer _uniformBuffer;
    private DeviceMemory _uniformBufferMemory;
    
    private Vector3 _cameraPosition = new Vector3(2.0f, 2.0f, 2.0f);
    private float _cameraYaw = MathF.PI * 1.25f;
    private float _cameraPitch = -MathF.PI * 0.2f;
    private Vector3 _cameraFront = Vector3.Zero;
    private Vector3 _cameraUp = Vector3.UnitY;
    private float _cameraSpeed = 1.0f;

    private Stopwatch _stopwatch;
    private int _frameCount;
    private double _elapsedTime;
    
    public void Run(string[] args) {
        Initialize(args);
        Dispose();
    }
        
    public void Initialize(string[] args) { 
        _compiler = new ShaderCompiler();
            
        _vkContext = new("vulkan");
        _sdlContext = new(SDL.InitFlags.Video | SDL.InitFlags.Events);
        _vkInstance = new(_vkContext, "magpieTests", "magpieco");
            
        _windowHandle = new("magpi", 400, 400, SDL.WindowFlags.Vulkan | SDL.WindowFlags.Resizable | SDL.WindowFlags.Transparent);
        _vkSurface = new(_vkInstance, _windowHandle.CreateVulkanSurface(_vkInstance));
        _windowHandle.SetRelativeMouseMode(true);
        
        _stopwatch = Stopwatch.StartNew();
        _frameCount = 0;
        _elapsedTime = 0.0;
            
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
        
        DescriptorSetLayoutBinding uboLayoutBinding = new(
            0,
            VkDescriptorType.UniformBuffer,
            1,
            VkShaderStageFlags.Vertex
        );
        _descriptorSetLayout = new(_vkDevice, new ReadOnlySpan<DescriptorSetLayoutBinding>(&uboLayoutBinding, 1));
            
        _pipeline = new Pipeline(
            _vkDevice,
            Graphics.MainSwapchain.Format,
            vertShaderCode.ToArray(),
            fragShaderCode.ToArray(),
            vertexInputBinding,
            vertexInputAttributs,
            _descriptorSetLayout
        );

        Vulkan.vkDestroyShaderModule(_vkDevice, vertmodule);
        Vulkan.vkDestroyShaderModule(_vkDevice, fragmodule);
            
        ReadOnlySpan<VertexPositionColor> sourceVertexData =
        [
            new VertexPositionColor(new Vector3(-0.5f, -0.5f, 0.0f), new Vector4(1.0f, 0.0f, 0.5f, 1.0f)),
            new VertexPositionColor(new Vector3(0.5f, -0.5f, 0.0f), new Vector4(0.0f, 1.0f, 0.3f, 1.0f)),
            new VertexPositionColor(new Vector3(0.5f, 0.5f, 0.0f), new Vector4(0.0f, 0.3f, 1.0f, 1.0f)),
            new VertexPositionColor(new Vector3(-0.5f, 0.5f, 0.0f), new Vector4(1.0f, 1.0f, 1.0f, 1.0f))
        ];
        uint vertexBufferSize = (uint)(sourceVertexData.Length * VertexPositionColor.SizeInBytes);

        ReadOnlySpan<uint> sourceIndexData = [0, 3, 2, 0, 2, 1];
        uint indexBufferSize = (uint)(sourceIndexData.Length * sizeof(uint));

        _vertexBuffer = new(_vkDevice, Graphics.GraphicsCommandPool, Graphics.GraphicsQueue, sourceVertexData);
        _indexBuffer = new(_vkDevice, Graphics.GraphicsCommandPool, Graphics.GraphicsQueue, MemoryMarshal.AsBytes(sourceIndexData));
        
        uint uboBufferSize = (uint)Marshal.SizeOf<UniformBufferObject>();

        _uniformBuffer = new Buffer(_vkDevice, uboBufferSize, VkBufferUsageFlags.UniformBuffer);
        _uniformBufferMemory = new DeviceMemory(_uniformBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);

        Span<DescriptorPoolSize> poolSizes = stackalloc DescriptorPoolSize[1];
        poolSizes[0] = new DescriptorPoolSize(VkDescriptorType.UniformBuffer, 1);
        _descriptorPool = new(_vkDevice, poolSizes, 1);

        _descriptorSet = _descriptorPool.AllocateDescriptorSet(_descriptorSetLayout);

        _descriptorSet.Update(_uniformBuffer, VkDescriptorType.UniformBuffer);
            
        while (!Quit) {
            Time.Start();
            while (SDL.PollEvent(out var @event)) {
                HandleEvent(@event);
            }
            HandleContinuousInput(); 
            
            Time.Update();
            Draw();
            Time.Stop();
            
            _frameCount++;
            _elapsedTime = _stopwatch.Elapsed.TotalSeconds; if(_elapsedTime >= 1.0) {
                double fps = _frameCount / _elapsedTime;
                _windowHandle.Title = $"FPS: {fps}";
                _frameCount = 0;
                _elapsedTime = 0.0;
                _stopwatch.Restart();
            }
        }
    }
    
    private void HandleEvent(SDL.Event @event) {
        switch (@event.Type) {
            case (uint)SDL.EventType.Quit:
                Quit = true;
                break;
            case (uint)SDL.EventType.KeyDown:
                if (@event.Key.Key == SDL.Keycode.Escape)
                    Quit = true;
                break;
            case (uint)SDL.EventType.MouseMotion:
                HandleMouseMotion((int)@event.Motion.XRel, (int)@event.Motion.YRel);
                break;
            case (uint)SDL.EventType.WindowMinimized:
                Graphics?.NotifyResize();
                break;
        }
    }
    
    private void HandleMouseMotion(int xRel, int yRel) {
        _cameraYaw += xRel * 0.005f;
        _cameraPitch -= yRel * 0.005f;
        _cameraPitch = Math.Clamp(_cameraPitch, -MathF.PI * 0.49f, MathF.PI * 0.49f);

        _cameraFront.X = MathF.Cos(_cameraYaw) * MathF.Cos(_cameraPitch);
        _cameraFront.Y = MathF.Sin(_cameraPitch);
        _cameraFront.Z = MathF.Sin(_cameraYaw) * MathF.Cos(_cameraPitch);
        _cameraFront = Vector3.Normalize(_cameraFront);
    }

    private void HandleContinuousInput() {
        float moveSpeed = _cameraSpeed * Time.DeltaTime;
        var keyboardState = SDL.GetKeyboardState(out int numKeys);

        Vector3 currentCameraFront = _cameraFront;
        Vector3 flatCameraFront = Vector3.Normalize(new Vector3(_cameraFront.X, 0, _cameraFront.Z));
        Vector3 right = Vector3.Normalize(Vector3.Cross(flatCameraFront, _cameraUp));

        if (keyboardState[(int)SDL.Scancode.W]) {
            _cameraPosition += flatCameraFront * moveSpeed;
        }
        if (keyboardState[(int)SDL.Scancode.S]) {
            _cameraPosition -= flatCameraFront * moveSpeed;
        }
        if (keyboardState[(int)SDL.Scancode.A]) {
            _cameraPosition -= right * moveSpeed; 
        }
        if (keyboardState[(int)SDL.Scancode.D]) {
            _cameraPosition += right * moveSpeed; 
        }
        if (keyboardState[(int)SDL.Scancode.Q]) {
            _cameraPosition += _cameraUp * moveSpeed; 
        }
        if (keyboardState[(int)SDL.Scancode.E]) {
            _cameraPosition -= _cameraUp * moveSpeed; 
        }
    }

    void Draw() {
        Debug.Assert(Graphics != null);

        float time = Time.GlobalTime;
        float r = (float)(Math.Sin(time * 0.5f) * 0.5f + 0.5f);
        float g = (float)(Math.Sin(time * 0.5f + 2) * 0.5f + 0.5f);
        float b = (float)(Math.Sin(time * 0.5f + 4) * 0.5f + 0.5f);
        var color = new Color(r, g, b);

        if(!Graphics.Begin(color)) return;
        
        UpdateUniformBuffer(time);
            
        var cmd = Graphics.RequestCurrentCommandBuffer();
            
        Vulkan.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _pipeline);
        fixed (VkDescriptorSet* pDescriptorSet = &_descriptorSet.Value) {
            Vulkan.vkCmdBindDescriptorSets(cmd, VkPipelineBindPoint.Graphics, _pipeline.Layout, 0, 1, pDescriptorSet, 0, null);
        }
            
        var extent = new Vector2(Graphics.MainSwapchain.Width, Graphics.MainSwapchain.Height);
        cmd.SetViewport(new(0, 0, extent.X, extent.Y));
        cmd.SetScissor(new(0, 0, (uint)extent.X, (uint)extent.Y));
            
        Vulkan.vkCmdBindVertexBuffer(cmd, 0, _vertexBuffer);
        Vulkan.vkCmdBindIndexBuffer(cmd, _indexBuffer, 0, VkIndexType.Uint32);
        Vulkan.vkCmdDrawIndexed(cmd, 6, 1, 0, 0, 0);

        Graphics.End();
    }
    
    private void UpdateUniformBuffer(float time) {
        Matrix4x4 model = Matrix4x4.CreateRotationX(time * 0.5f) * Matrix4x4.CreateRotationZ(time * 0.5f) * Matrix4x4.CreateRotationY(time * 0.5f);
        Matrix4x4 view = Matrix4x4.CreateLookAt(_cameraPosition, _cameraPosition + _cameraFront, _cameraUp);

        var extent = new Vector2(Graphics.MainSwapchain.Width, Graphics.MainSwapchain.Height);
        float aspectRatio = extent.X / extent.Y;
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f, 
            aspectRatio,
            0.1f, 
            100.0f 
        );

        proj.M22 *= -1; 

        UniformBufferObject ubo = new()
        {
            Model = model,
            View = view,
            Proj = proj
        };

        Span<UniformBufferObject> mappedUbo = _uniformBufferMemory.Map<UniformBufferObject>(1);
        mappedUbo[0] = ubo;
        _uniformBufferMemory.Unmap();
    }

    private void Dispose() {
        Vulkan.vkDeviceWaitIdle(_vkDevice);
            
        _pipeline.Dispose();

        _vertexBuffer.Dispose();       
        _indexBuffer.Dispose();       
        
        _uniformBuffer.Dispose();
        _uniformBufferMemory.Dispose();
        _descriptorPool.Dispose();
        _descriptorSetLayout.Dispose();
            
        Graphics!.Dispose();
            
        _vkDevice.Dispose();
        _vkInstance.Dispose();
        _vkContext?.Dispose();
        _sdlContext?.Dispose();
        _windowHandle.Dispose();
    }
}