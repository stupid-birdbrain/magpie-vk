using Magpie;
using System.Diagnostics;
using System.Numerics;
using Magpie.Core;
using Magpie.Utilities;
using SDL3;
using ShaderCompilation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using StainedGlass;
using Standard;
using System.Runtime.InteropServices;
using Vortice.Vulkan;
using Buffer = Magpie.Core.Buffer;
using Color = Standard.Color;
using Image = Magpie.Core.Image;

namespace Samples;

[StructLayout(LayoutKind.Sequential)]
public struct PushConstantMatrices {
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
    private PipelineLayout _pipelineLayout;

    private VertexBuffer<VertexPositionColorTexture> _vertexBuffer;
    private IndexBuffer _indexBuffer;

    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _descriptorSet;

    private Vector3 _cameraPosition = new Vector3(2.0f, 2.0f, 2.0f);
    private float _cameraYaw = MathF.PI * 1.25f;
    private float _cameraPitch = -MathF.PI * 0.2f;
    private Vector3 _cameraFront = Vector3.Zero;
    private Vector3 _cameraUp = Vector3.UnitY;
    private float _cameraSpeed = 1.0f;

    private Image _textureImage;
    private DeviceMemory _textureImageMemory;
    private ImageView _textureImageView;
    private Sampler _textureSampler;

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

        _windowHandle = new("magpi", 1200, 800, SDL.WindowFlags.Vulkan | SDL.WindowFlags.Resizable);
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
        
        if (!_vkInstance.TryGetBestPhysicalDevice(requiredDeviceExtensions, out PhysicalDevice bestDevice)) {
            throw new InvalidOperationException("no valid physical device found");
        }

        if (!bestDevice.TryGetGraphicsQueueFamily(out uint graphicsQueueFamilyIndex)) {
            throw new Exception("selected physical device does not have a graphics queue family");
        }

        _vkDevice = new(bestDevice, [graphicsQueueFamilyIndex], requiredDeviceExtensions);
        Console.WriteLine("selected physical device info:" + bestDevice.ToString());

        Graphics = new (_vkInstance, _vkSurface, bestDevice, _vkDevice);
        CreateTextureImage("resources/hashbrown.png");

        var entryPoint = "main"u8;

        var vertShaderCode = _compiler.CompileShader(@"resources/base/base.vert", ShaderKind.Vertex, true);
        var fragShaderCode = _compiler.CompileShader(@"resources/base/base.frag", ShaderKind.Fragment, true);

        var data = _compiler.ReflectShader(_compiler.CompileShader(@"resources/base_textured/base.frag", ShaderKind.Fragment, true).ToArray());
        Console.WriteLine(data.ToString());
        
        // foreach(var pc in data.PushConstants) {
        //     Console.WriteLine(pc.ToString());
        //     foreach(var member in pc.Members) {
        //         Console.WriteLine(member.ToString());
        //     }
        // }
        //
        // foreach(var ubo in data.UniformBuffers) {
        //     Console.WriteLine(ubo.ToString());
        // }
        //
        // foreach(var ssbo in data.StorageBuffers) {
        //     foreach(var member in ssbo.Members) {
        //         Console.WriteLine(member.ToString());
        //     }
        //     Console.WriteLine(ssbo.ToString());
        // }
        
        var vertmodule = new ShaderModule(_vkDevice, vertShaderCode.ToArray());
        var fragmodule = new ShaderModule(_vkDevice, fragShaderCode.ToArray());
        
        VkVertexInputBindingDescription vertexInputBinding = new((uint)VertexPositionColorTexture.SizeInBytes);
        ReadOnlySpan<VkVertexInputAttributeDescription> vertexInputAttributes = stackalloc VkVertexInputAttributeDescription[3] {
            new(
                location: 0,
                binding: 0,
                format: Vector3.AsFormat(),
                offset: (uint)Marshal.OffsetOf<VertexPositionColorTexture>(nameof(VertexPositionColorTexture.Position))
            ),
            new(
                location: 1,
                binding: 0,
                format: Vector3.AsFormat(),
                offset: (uint)Marshal.OffsetOf<VertexPositionColorTexture>(nameof(VertexPositionColorTexture.Color))
            ),
            new(
                location: 2,
                binding: 0,
                format: Vector2.AsFormat(),
                offset: (uint)Marshal.OffsetOf<VertexPositionColorTexture>(nameof(VertexPositionColorTexture.TexCoord))
            )
        };

        Span<DescriptorSetLayoutBinding> descriptorSetBindings = stackalloc DescriptorSetLayoutBinding[1];
        descriptorSetBindings[0] = new(0, VkDescriptorType.CombinedImageSampler, 1, VkShaderStageFlags.Fragment);
        _descriptorSetLayout = new(_vkDevice, descriptorSetBindings);

        VkPushConstantRange vkPushConstantRange = new VkPushConstantRange {
            stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment,
            offset = 0,
            size = (uint)Marshal.SizeOf<PushConstantMatrices>()
        };
        var pushConstant = new PushConstant(vkPushConstantRange.offset, vkPushConstantRange.size, vkPushConstantRange.stageFlags);

        _pipelineLayout = new(_vkDevice, [_descriptorSetLayout], [pushConstant]);
        
        PipelineCreationDescription pipelineDescription = new() {
            VertexShader = vertmodule,
            FragmentShader = fragmodule,

            BlendSettings = BlendSettings.Opaque,
            DepthTestEnable = true,
            DepthWriteEnable = true,
            DepthCompareOp = VkCompareOp.Less,
            StencilTestEnable = false,
            
            CullMode = VkCullModeFlags.None,
            FrontFace = VkFrontFace.CounterClockwise,
            PolygonMode = VkPolygonMode.Fill,

            PrimitiveTopology = VkPrimitiveTopology.TriangleList,
            PrimitiveRestartEnable = false
        };

        _pipeline = new Pipeline(
            _vkDevice,
            Graphics.MainSwapchain.Format,
            Graphics.DepthImage.Format,
            pipelineDescription,
            _pipelineLayout,
            vertexInputBinding,
            vertexInputAttributes,
            "main"u8
        );

        Vulkan.vkDestroyShaderModule(_vkDevice, vertmodule);
        Vulkan.vkDestroyShaderModule(_vkDevice, fragmodule);

        ReadOnlySpan<VertexPositionColorTexture> sourceVertexData =
        [
            // Front face
            new VertexPositionColorTexture(new Vector3(-0.5f, -0.5f, 0.5f), Colors.Red.ToVector4(), new Vector2(0.0f, 0.0f)), // 0
            new VertexPositionColorTexture(new Vector3(0.5f, -0.5f, 0.5f), Colors.Aqua.ToVector4(), new Vector2(1.0f, 0.0f)), // 1
            new VertexPositionColorTexture(new Vector3(0.5f, 0.5f, 0.5f), Colors.White.ToVector4(), new Vector2(1.0f, 1.0f)), // 2
            new VertexPositionColorTexture(new Vector3(-0.5f, 0.5f, 0.5f), Colors.White.ToVector4(), new Vector2(0.0f, 1.0f)), // 3

            // Back face
            new VertexPositionColorTexture(new Vector3(0.5f, -0.5f, -0.5f), Colors.White.ToVector4(), new Vector2(0.0f, 0.0f)), // 4
            new VertexPositionColorTexture(new Vector3(-0.5f, -0.5f, -0.5f), Colors.White.ToVector4(), new Vector2(1.0f, 0.0f)), // 5
            new VertexPositionColorTexture(new Vector3(-0.5f, 0.5f, -0.5f), Colors.White.ToVector4(), new Vector2(1.0f, 1.0f)), // 6
            new VertexPositionColorTexture(new Vector3(0.5f, 0.5f, -0.5f), Colors.White.ToVector4(), new Vector2(0.0f, 1.0f)), // 7

            // Top face
            new VertexPositionColorTexture(new Vector3(-0.5f, 0.5f, 0.5f), Colors.White.ToVector4(), new Vector2(0.0f, 0.0f)), // 8
            new VertexPositionColorTexture(new Vector3(0.5f, 0.5f, 0.5f), Colors.White.ToVector4(), new Vector2(1.0f, 0.0f)), // 9
            new VertexPositionColorTexture(new Vector3(0.5f, 0.5f, -0.5f), Colors.White.ToVector4(), new Vector2(1.0f, 1.0f)), // 10
            new VertexPositionColorTexture(new Vector3(-0.5f, 0.5f, -0.5f), Colors.White.ToVector4(), new Vector2(0.0f, 1.0f)), // 11

            // Bottom face
            new VertexPositionColorTexture(new Vector3(-0.5f, -0.5f, -0.5f), Colors.White.ToVector4(), new Vector2(0.0f, 0.0f)), // 12
            new VertexPositionColorTexture(new Vector3(0.5f, -0.5f, -0.5f), Colors.White.ToVector4(), new Vector2(1.0f, 0.0f)), // 13
            new VertexPositionColorTexture(new Vector3(0.5f, -0.5f, 0.5f), Colors.White.ToVector4(), new Vector2(1.0f, 1.0f)), // 14
            new VertexPositionColorTexture(new Vector3(-0.5f, -0.5f, 0.5f), Colors.White.ToVector4(), new Vector2(0.0f, 1.0f)), // 15

            // Right face
            new VertexPositionColorTexture(new Vector3(0.5f, -0.5f, 0.5f), Colors.White.ToVector4(), new Vector2(0.0f, 0.0f)), // 16
            new VertexPositionColorTexture(new Vector3(0.5f, -0.5f, -0.5f), Colors.White.ToVector4(), new Vector2(1.0f, 0.0f)), // 17
            new VertexPositionColorTexture(new Vector3(0.5f, 0.5f, -0.5f), Colors.White.ToVector4(), new Vector2(1.0f, 1.0f)), // 18
            new VertexPositionColorTexture(new Vector3(0.5f, 0.5f, 0.5f), Colors.White.ToVector4(), new Vector2(0.0f, 1.0f)), // 19

            // Left face
            new VertexPositionColorTexture(new Vector3(-0.5f, -0.5f, -0.5f), Colors.White.ToVector4(), new Vector2(0.0f, 0.0f)), // 20
            new VertexPositionColorTexture(new Vector3(-0.5f, -0.5f, 0.5f), Colors.White.ToVector4(), new Vector2(1.0f, 0.0f)), // 21
            new VertexPositionColorTexture(new Vector3(-0.5f, 0.5f, 0.5f), Colors.White.ToVector4(), new Vector2(1.0f, 1.0f)), // 22
            new VertexPositionColorTexture(new Vector3(-0.5f, 0.5f, -0.5f), Colors.White.ToVector4(), new Vector2(0.0f, 1.0f)) // 23
        ];
        
        ReadOnlySpan<uint> sourceIndexData =
        [
            0, 1, 2, 2, 3, 0,
            4, 5, 6, 6, 7, 4,
            8, 9, 10, 10, 11, 8,
            12, 13, 14, 14, 15, 12,
            16, 17, 18, 18, 19, 16,
            20, 21, 22, 22, 23, 20
        ];

        uint vertexBufferSize = (uint)(sourceVertexData.Length * VertexPositionColorTexture.SizeInBytes);
        uint indexBufferSize = (uint)(sourceIndexData.Length * sizeof(uint));

        _vertexBuffer = new(_vkDevice, Graphics.GraphicsCommandPool, Graphics.GraphicsQueue, sourceVertexData);
        _indexBuffer = new(_vkDevice, Graphics.GraphicsCommandPool, Graphics.GraphicsQueue, MemoryMarshal.AsBytes(sourceIndexData));

        Span<DescriptorPoolSize> poolSizes = stackalloc DescriptorPoolSize[1];
        poolSizes[0] = new DescriptorPoolSize(VkDescriptorType.CombinedImageSampler, 1);
        _descriptorPool = new(_vkDevice, poolSizes, 1);

        _descriptorSet = _descriptorPool.AllocateDescriptorSet(_descriptorSetLayout);

        _descriptorSet.Update(_textureImageView, _textureSampler, VkDescriptorType.CombinedImageSampler);

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
                double averageFrametimeMs = (_elapsedTime / _frameCount) * 1000.0;
                _windowHandle.Title = $"FPS: {Math.Round(fps)}, {averageFrametimeMs:F2}ms";
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

        if(!Graphics.Begin(Colors.SlateGray)) return;

        UpdatePushConstants(time);

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
        Vulkan.vkCmdDrawIndexed(cmd, _indexBuffer.IndexCount, 1, 0, 0, 0);

        Graphics.End();
    }

    private void CreateTextureImage(string path) {
        using Image<Rgba32> imageSharp = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
        imageSharp.Mutate(x => x.Flip(FlipMode.Vertical));

        uint imageSize = (uint)(imageSharp.Width * imageSharp.Height * 4);
        var pixelData = new byte[imageSize];
        imageSharp.CopyPixelDataTo(pixelData);
        ReadOnlySpan<byte> pixelSpan = pixelData;

        using var stagingBuffer = new Buffer(_vkDevice, imageSize, VkBufferUsageFlags.TransferSrc);
        using var stagingMemory = new DeviceMemory(stagingBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        stagingMemory.CopyFrom(pixelSpan);

        _textureImage = new Image(
            _vkDevice,
            (uint)imageSharp.Width,
            (uint)imageSharp.Height,
            1,
            VkFormat.R8G8B8A8Unorm,
            VkImageUsageFlags.TransferDst | VkImageUsageFlags.Sampled
        );

        _textureImageMemory = new DeviceMemory(_textureImage, VkMemoryPropertyFlags.DeviceLocal);

        {
            using var fence = Graphics!.RequestFence(VkFenceCreateFlags.None);
            var cmd = Graphics.AllocateCommandBuffer(true);
            cmd.Begin();

            cmd.TransitionImageLayout(_textureImage, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal);
            cmd.CopyBufferToImage(stagingBuffer, _textureImage, (uint)imageSharp.Width, (uint)imageSharp.Height, 0, 0);
            cmd.TransitionImageLayout(_textureImage, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal);

            cmd.End();
            Graphics.Submit(cmd, fence);
            fence.Wait();
            cmd.Dispose();
        }

        _textureImageView = new ImageView(_textureImage);
        _textureSampler = new Sampler(_vkDevice, new SamplerCreateParameters(VkFilter.Nearest, VkSamplerAddressMode.Repeat));
    }

    private void UpdatePushConstants(float time) {
        //var model = Matrix4x4.CreateRotationX(time * 0.5f) * Matrix4x4.CreateRotationZ(time * 0.5f) * Matrix4x4.CreateRotationY(time * 0.5f);
        var view = Matrix4x4.CreateLookAt(_cameraPosition, _cameraPosition + _cameraFront, _cameraUp);

        var extent = new Vector2(Graphics.MainSwapchain.Width, Graphics.MainSwapchain.Height);
        float aspectRatio = extent.X / extent.Y;
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f,
            aspectRatio,
            0.1f,
            100.0f
        );

        proj.M22 *= -1;

        PushConstantMatrices pc = new() {
            Model = Matrix4x4.Identity,
            View = view,
            Proj = proj
        };

        var cmd = Graphics!.RequestCurrentCommandBuffer();
        Vulkan.vkCmdPushConstants(
            cmd,
            _pipeline.Layout,
            VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment,
            0,
            (uint)Marshal.SizeOf<PushConstantMatrices>(),
            &pc
        );
    }

    private void Dispose() {
        Vulkan.vkDeviceWaitIdle(_vkDevice);

        _textureSampler.Dispose();
        _textureImageView.Dispose();
        _textureImage.Dispose();
        _textureImageMemory.Dispose();

        _pipeline.Dispose();
        _pipelineLayout.Dispose();

        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();

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