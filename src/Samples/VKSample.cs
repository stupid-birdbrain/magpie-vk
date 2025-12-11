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
public struct PushConstants {
    public Matrix4x4 View;
    public Matrix4x4 Proj;
}

[StructLayout(LayoutKind.Sequential)]
public struct InstanceTransform {
    public Matrix4x4 Model;
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
    
    private const int MAX_INSTANCES = 12;
    private int _instanceCount = 12;
    private InstanceTransform[] _instanceTransforms = new InstanceTransform[MAX_INSTANCES];
    private Buffer _instanceBuffer;
    private DeviceMemory _instanceMemory;

    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _descriptorSet;

    private Vector3 _cameraPosition = new Vector3(2.0f, 2.0f, 2.0f);
    private float _cameraYaw = MathF.PI * 1.25f;
    private float _cameraPitch = -MathF.PI * 0.2f;
    private Vector3 _cameraFront = Vector3.Zero;
    private Vector3 _cameraUp = Vector3.UnitY;
    private float _cameraSpeed = 1.0f;

    private SpriteBatch? _spriteBatch;
    private SpriteTexture? _sprite;

    private Stopwatch _stopwatch;
    private int _frameCount;
    private double _elapsedTime;

    private Keyboard _keyboard;
    private Mouse _mouse;
    private bool _isRelativeMouseMode = true;

    public void Run(string[] args) {
        Initialize(args);
        Dispose();
    }

    public void Initialize(string[] args) {
        _compiler = new ShaderCompiler();

        _vkContext = new("vulkan");
        _sdlContext = new(SDL.InitFlags.Video | SDL.InitFlags.Events | SDL.InitFlags.Gamepad);
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
            new VkUtf8String(Vulkan.VK_KHR_DEFERRED_HOST_OPERATIONS_EXTENSION_NAME.GetPointer()).ToString()!,
            "VK_KHR_spirv_1_4",
            "VK_KHR_dynamic_rendering"
        ];

        PhysicalDevice bestDevice = default;
        if(args.Contains("--override")) { // --override <index>
            int idx = Array.IndexOf(args, "--override") + 1;
            if(idx >= args.Length) {
                throw new ArgumentException("no index provided for --override argument");
            }
            
            if(!int.TryParse(args[idx], out int deviceIndex)) {
                throw new ArgumentException("invalid index provided for --override argument");
            }
            
            if(!_vkInstance.TryGetPhysicalDeviceByIndex(deviceIndex, requiredDeviceExtensions, out bestDevice)) {
                throw new InvalidOperationException($"no valid physical device found at index {deviceIndex}");
            }
        }
        else {
            if(!_vkInstance.TryGetBestPhysicalDevice(requiredDeviceExtensions, out bestDevice)) {
                throw new InvalidOperationException("no valid physical device found");
            }
        }

        if (!bestDevice.TryGetGraphicsQueueFamily(out uint graphicsQueueFamilyIndex)) {
            throw new Exception("selected physical device does not have a graphics queue family");
        }

        _vkDevice = new(bestDevice, [graphicsQueueFamilyIndex], requiredDeviceExtensions);
        Console.WriteLine("selected physical device info:" + bestDevice.ToString());

        Graphics = new (_vkInstance, _vkSurface, bestDevice, _vkDevice);
        _sprite = CreateTextureImage("resources/hashbrown.png");

        var entryPoint = "main"u8;

        var vertShaderCode = _compiler.CompileShader(@"resources/base/base.vert", ShaderKind.Vertex, true);
        var fragShaderCode = _compiler.CompileShader(@"resources/base/base.frag", ShaderKind.Fragment, true);

        var data = _compiler.ReflectShader(_compiler.CompileShader(@"resources/base_textured/base.frag", ShaderKind.Fragment, true).ToArray());
        Console.WriteLine(data.ToString());
        
        var vertmodule = new ShaderModule(_vkDevice, vertShaderCode.ToArray());
        var fragmodule = new ShaderModule(_vkDevice, fragShaderCode.ToArray());
        
        VkVertexInputBindingDescription vertexInputBinding = new((uint)VertexPositionColorTexture.SizeInBytes);
        ReadOnlySpan<VkVertexInputAttributeDescription> vertexInputAttributes = stackalloc VkVertexInputAttributeDescription[3] {
            new(location: 0, binding: 0, format: Vector3.AsFormat(), offset: (uint)Marshal.OffsetOf<VertexPositionColorTexture>(nameof(VertexPositionColorTexture.Position))),
            new(location: 1, binding: 0, format: Vector3.AsFormat(), offset: (uint)Marshal.OffsetOf<VertexPositionColorTexture>(nameof(VertexPositionColorTexture.Color))),
            new(location: 2, binding: 0, format: Vector2.AsFormat(), offset: (uint)Marshal.OffsetOf<VertexPositionColorTexture>(nameof(VertexPositionColorTexture.TexCoord)))
        };

        uint instanceBufferSize = (uint)(Marshal.SizeOf<InstanceTransform>() * MAX_INSTANCES);
        _instanceBuffer = new(_vkDevice, instanceBufferSize, VkBufferUsageFlags.StorageBuffer);
        _instanceMemory = new DeviceMemory(_instanceBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);

        Span<DescriptorSetLayoutBinding> descriptorSetBindings = stackalloc DescriptorSetLayoutBinding[2];
        descriptorSetBindings[0] = new(0, VkDescriptorType.CombinedImageSampler, 1, VkShaderStageFlags.Fragment);
        descriptorSetBindings[1] = new(1, VkDescriptorType.StorageBuffer, 1, VkShaderStageFlags.Vertex);
        _descriptorSetLayout = new(_vkDevice, descriptorSetBindings);

        VkPushConstantRange vkPushConstantRange = new VkPushConstantRange {
            stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment,
            offset = 0,
            size = (uint)Marshal.SizeOf<PushConstants>()
        };
        var pushConstant = new PushConstant(vkPushConstantRange.offset, vkPushConstantRange.size, vkPushConstantRange.stageFlags);

        _pipelineLayout = new(_vkDevice, [_descriptorSetLayout], [pushConstant]);
        
        PipelineCreationDescription pipelineDescription = new() {
            VertexShader = vertmodule,
            FragmentShader = fragmodule,

            BlendSettings = BlendSettings.AlphaBlend,
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

        Span<DescriptorPoolSize> poolSizes = stackalloc DescriptorPoolSize[2];
        poolSizes[0] = new DescriptorPoolSize(VkDescriptorType.CombinedImageSampler, 1);
        poolSizes[1] = new DescriptorPoolSize(VkDescriptorType.StorageBuffer, MAX_INSTANCES);
        _descriptorPool = new(_vkDevice, poolSizes, 1);

        _descriptorSet = _descriptorPool.AllocateDescriptorSet(_descriptorSetLayout);

        if (_sprite is null) {
            throw new InvalidOperationException("texture was not created before descriptor setup.");
        }

        _descriptorSet.Update(_sprite.ImageView, _sprite.Sampler, VkDescriptorType.CombinedImageSampler);
        _descriptorSet.Update(_instanceBuffer, VkDescriptorType.StorageBuffer, 1);

        var spriteVertexCode = _compiler.CompileShader("resources/spritebatch/spritebatch.vert", ShaderKind.Vertex, true).ToArray();
        var spriteFragmentCode = _compiler.CompileShader("resources/spritebatch/spritebatch.frag", ShaderKind.Fragment, true).ToArray();
        _spriteBatch = new SpriteBatch(Graphics!, spriteVertexCode, spriteFragmentCode, 256);

        _keyboard = new();
        _mouse = new();
        
        while (!Quit) {
            _keyboard.Update();
            _mouse.Update();
            
            Time.Start();
            while (_sdlContext.PollEvent(out var @event)) {
                HandleEvent(@event);

                switch(@event.Type) {
                    case (uint)SDL.EventType.KeyDown:
                        _keyboard.SetKeyState(@event.Key.Scancode, true);
                        Console.WriteLine($"key pressed: {@event.Key.Scancode}");
                        break;
                    case (uint)SDL.EventType.KeyUp:
                        _keyboard.SetKeyState(@event.Key.Scancode, false);
                        break;
                    case (uint)SDL.EventType.MouseMotion:
                        _mouse.SetPosition(new Vector2(@event.Motion.X, @event.Motion.Y));
                        if (_isRelativeMouseMode) {
                            _mouse.AddRelativeDelta(new Vector2(@event.Motion.XRel, @event.Motion.YRel));
                        }
                        
                        break;
                    case (uint)SDL.EventType.MouseButtonDown:
                        _mouse.SetButtonState((Mouse.Button)@event.Button.Button, true);
                        break;
                    case (uint)SDL.EventType.MouseButtonUp:
                        _mouse.SetButtonState((Mouse.Button)@event.Button.Button, false);
                        break;
                    case (uint)SDL.EventType.MouseWheel:
                        _mouse.AddScrollDelta(new Vector2(@event.Wheel.X, @event.Wheel.Y));
                        break;
                }
            }
            
            if(_mouse.WasButtonReleased(Mouse.Button.RightButton))
                Console.WriteLine("no longer pressed");
            
            var scrollDelta = _mouse.GetScrollDelta();
            if (scrollDelta != Vector2.Zero) {
                Console.WriteLine($"scroll: {scrollDelta}");
            }
            
            HandleContinuousInput();
            UpdateCameraMovement();
            
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
                break;
            case (uint)SDL.EventType.WindowMinimized:
                Graphics?.NotifyResize();
                break;
        }
    }

    private void UpdateCameraMovement() {
        if (_isRelativeMouseMode) {
            Vector2 mouseDelta = _mouse.GetRelativeDelta();

            _cameraYaw += mouseDelta.X * 0.005f;
            _cameraPitch -= mouseDelta.Y * 0.005f;

            _cameraPitch = Math.Clamp(_cameraPitch, -MathF.PI * 0.49f, MathF.PI * 0.49f);
            
            _cameraFront.X = MathF.Cos(_cameraYaw) * MathF.Cos(_cameraPitch);
            _cameraFront.Y = MathF.Sin(_cameraPitch);
            _cameraFront.Z = MathF.Sin(_cameraYaw) * MathF.Cos(_cameraPitch);
            _cameraFront = Vector3.Normalize(_cameraFront);
        }
    }

    private void HandleContinuousInput() {
        float moveSpeed = _cameraSpeed * Time.DeltaTime * 6;

        Vector3 currentCameraFront = _cameraFront;
        Vector3 flatCameraFront = Vector3.Normalize(new Vector3(_cameraFront.X, 0, _cameraFront.Z));
        Vector3 right = Vector3.Normalize(Vector3.Cross(flatCameraFront, _cameraUp));
    
        if (_keyboard.IsKeyDown(Keyboard.Keys.W)) {
            _cameraPosition += flatCameraFront * moveSpeed;
        }
        if (_keyboard.IsKeyDown(Keyboard.Keys.S)) {
            _cameraPosition -= flatCameraFront * moveSpeed;
        }
        if (_keyboard.IsKeyDown(Keyboard.Keys.A)) {
            _cameraPosition -= right * moveSpeed;
        }
        if (_keyboard.IsKeyDown(Keyboard.Keys.D)) {
            _cameraPosition += right * moveSpeed;
        }
        if (_keyboard.IsKeyDown(Keyboard.Keys.Q)) {
            _cameraPosition += _cameraUp * moveSpeed;
        }
        if (_keyboard.IsKeyDown(Keyboard.Keys.E)) {
            _cameraPosition -= _cameraUp * moveSpeed;
        }
    }

    void Draw() {
        var graphics = Graphics;
        Debug.Assert(graphics != null);
        Debug.Assert(_spriteBatch != null);
        
        if (graphics is null) {
            return;
        }

        if(!graphics.Begin(Colors.LightGray)) return;

        UpdateShaderData();

        var cmd = graphics.RequestCurrentCommandBuffer();

        cmd.BindPipeline(_pipeline);
        
        Span<DescriptorSet> descriptorSets = stackalloc DescriptorSet[1];
        descriptorSets[0] = _descriptorSet;

        cmd.BindDescriptorSets(_pipelineLayout, descriptorSets);

        var extent = new Vector2(graphics.MainSwapchain.Width, graphics.MainSwapchain.Height);
        cmd.SetViewport(new(0, 0, extent.X, extent.Y));
        cmd.SetScissor(new(0, 0, (uint)extent.X, (uint)extent.Y));

        cmd.BindVertexBuffer(_vertexBuffer);
        cmd.BindIndexBuffer(_indexBuffer);
        
        cmd.DrawIndexed(_indexBuffer.IndexCount, (uint)_instanceCount);

        using (var sb = _spriteBatch.Begin(sortMode: SpriteSortMode.Deferred)) {
            sb.Draw(new() { Texture = _sprite, Position = new Vector2(32f), Color = Colors.White});
        }

        graphics.End();
    }

    private SpriteTexture CreateTextureImage(string path) {
        using Image<Rgba32> imageSharp = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
        //imageSharp.Mutate(x => x.Flip(FlipMode.Vertical));

        uint imageSize = (uint)(imageSharp.Width * imageSharp.Height * 4);
        var pixelData = new byte[imageSize];
        imageSharp.CopyPixelDataTo(pixelData);
        ReadOnlySpan<byte> pixelSpan = pixelData;

        using var stagingBuffer = new Buffer(_vkDevice, imageSize, VkBufferUsageFlags.TransferSrc);
        using var stagingMemory = new DeviceMemory(stagingBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        stagingMemory.CopyFrom(pixelSpan);

        Image textureImage = new Image(
            _vkDevice,
            (uint)imageSharp.Width,
            (uint)imageSharp.Height,
            1,
            VkFormat.R8G8B8A8Unorm,
            VkImageUsageFlags.TransferDst | VkImageUsageFlags.Sampled
        );

        DeviceMemory textureMemory = new DeviceMemory(textureImage, VkMemoryPropertyFlags.DeviceLocal);

        {
            using var fence = Graphics!.RequestFence(VkFenceCreateFlags.None);
            var cmd = Graphics.AllocateCommandBuffer(true);
            cmd.Begin();

            cmd.TransitionImageLayout(textureImage, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal);
            cmd.CopyBufferToImage(stagingBuffer, textureImage, (uint)imageSharp.Width, (uint)imageSharp.Height, 0, 0);
            cmd.TransitionImageLayout(textureImage, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal);

            cmd.End();
            Graphics.Submit(cmd, fence);
            fence.Wait();
            cmd.Dispose();
        }

        ImageView textureView = new ImageView(textureImage);
        Sampler textureSampler = new Sampler(_vkDevice, new SamplerCreateParameters(VkFilter.Nearest, VkSamplerAddressMode.Repeat));

        return new SpriteTexture(textureImage, textureMemory, textureView, textureSampler);
    }

    private void UpdateShaderData() {
        var graphics = Graphics!;
        var view = Matrix4x4.CreateLookAt(_cameraPosition, _cameraPosition + _cameraFront, _cameraUp);
        var extent = new Vector2(graphics.MainSwapchain.Width, graphics.MainSwapchain.Height);
        float aspectRatio = extent.X / extent.Y;
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 1.5f,
            aspectRatio,
            0.1f,
            100.0f
        );
        proj.M22 *= -1;

        PushConstants globalPushConstants = new() {
            View = view,
            Proj = proj
        };

        var cmd = graphics.RequestCurrentCommandBuffer();
        Vulkan.vkCmdPushConstants(
            cmd,
            _pipelineLayout,
            VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment,
            0,
            (uint)Marshal.SizeOf<PushConstants>(),
            &globalPushConstants
        );
        
        float currentTime = Time.GlobalTime;
        int sideLength = (int)MathF.Floor(MathF.Pow(MAX_INSTANCES, 1.0f / 3.0f));
        float cubeSpacing = 1.1f;
        float totalSideLength = sideLength * cubeSpacing;
        float offset = -totalSideLength / 2.0f + cubeSpacing / 2.0f; 

        int instanceIndex = 0;
        for (int x = 0; x < sideLength; x++) {
            for (int y = 0; y < sideLength; y++) {
                for (int z = 0; z < sideLength; z++) {
                    if (instanceIndex >= _instanceCount) break;

                    var position = new Vector3(
                        x * cubeSpacing + offset,
                        y * cubeSpacing + offset,
                        z * cubeSpacing + offset
                    );

                    var translation = Matrix4x4.CreateTranslation(position);
                    
                    var scale = Matrix4x4.CreateScale(0.35f);

                    _instanceTransforms[instanceIndex].Model = scale * translation;
                    instanceIndex++;
                }
                if (instanceIndex >= _instanceCount) break;
            }
            if (instanceIndex >= _instanceCount) break;
        }

        _instanceMemory.CopyFrom(new ReadOnlySpan<InstanceTransform>(_instanceTransforms, 0, _instanceCount));
    }

    private void Dispose() {
        Vulkan.vkDeviceWaitIdle(_vkDevice);

        _spriteBatch?.Dispose();
        _sprite?.Dispose();

        _pipeline.Dispose();
        _pipelineLayout.Dispose();

        _instanceBuffer.Dispose();
        _instanceMemory.Dispose();

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