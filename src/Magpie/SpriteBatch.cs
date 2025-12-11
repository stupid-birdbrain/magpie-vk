
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Magpie.Core;
using Magpie.Utilities;
using Standard;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie;

public enum SpriteSortMode {
    Deferred,
    Immediate
}

[Flags]
public enum SpriteEffects {
    None = 0,
    FlipHorizontally = 1,
    FlipVertically = 2
}

public sealed class SpriteBatch : IDisposable {
    private readonly GraphicsDevice _graphicsDevice;
    private readonly LogicalDevice _device;
    private readonly int _initialCapacity;

    private Pipeline _pipeline;
    private PipelineLayout _pipelineLayout;
    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorPool _descriptorPool;
    private DescriptorSet[] _descriptorSets; 

    private VertexBuffer<SpriteVertex> _vertexBuffer;
    private IndexBuffer _indexBuffer;
    
    private SpriteVertex[] _vertexScratch = Array.Empty<SpriteVertex>();
    private uint[] _indexScratch = Array.Empty<uint>();

    private int _spriteCapacity;
    private int _spriteCount;
    private SpriteTexture? _currentTexture;
    private bool _isActive;
    private bool _buffersInitialized;
    private SpriteSortMode _sortMode;
    private Matrix4x4 _transform;
    private CmdBuffer _commandBuffer;

    public struct DrawSettings {
        public SpriteTexture Texture;
        public Vector2 Position;
        public Rectangle? SourceRectangle;
        public Color Color;
        public float Rotation;
        public Vector2 Origin;
        public Vector2 Scale;
        public SpriteEffects Effects;
        public float LayerDepth;
    }

    public SpriteBatch(GraphicsDevice graphicsDevice, ReadOnlySpan<byte> vertexShaderCode, ReadOnlySpan<byte> fragmentShaderCode, int initialSpriteCapacity = 256) {
        if (graphicsDevice is null) {
            throw new ArgumentNullException(nameof(graphicsDevice));
        }
        if (vertexShaderCode.IsEmpty) {
            throw new ArgumentException("Vertex shader code cannot be empty.", nameof(vertexShaderCode));
        }
        if (fragmentShaderCode.IsEmpty) {
            throw new ArgumentException("Fragment shader code cannot be empty.", nameof(fragmentShaderCode));
        }

        _graphicsDevice = graphicsDevice;
        _device = graphicsDevice.LogicalDevice;
        _initialCapacity = Math.Max(1, initialSpriteCapacity);

        using ShaderModule vertexModule = new(_device, vertexShaderCode.ToArray());
        using ShaderModule fragmentModule = new(_device, fragmentShaderCode.ToArray());

        _descriptorSetLayout = new DescriptorSetLayout(_device, 0, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment);

        VkPushConstantRange pushConstantRange = new() {
            stageFlags = VkShaderStageFlags.Vertex,
            offset = 0,
            size = (uint)Unsafe.SizeOf<Matrix4x4>()
        };

        PushConstant pushConstant = new(pushConstantRange.offset, pushConstantRange.size, pushConstantRange.stageFlags);
        _pipelineLayout = new PipelineLayout(_device, [_descriptorSetLayout], [pushConstant]);

        PipelineCreationDescription pipelineDescription = new() {
            VertexShader = vertexModule,
            FragmentShader = fragmentModule,
            BlendSettings = BlendSettings.AlphaBlend,
            DepthTestEnable = false, 
            DepthWriteEnable = false,
            DepthCompareOp = VkCompareOp.Always,
            StencilTestEnable = false,
            CullMode = VkCullModeFlags.None,
            FrontFace = VkFrontFace.CounterClockwise,
            PolygonMode = VkPolygonMode.Fill,
            PrimitiveTopology = VkPrimitiveTopology.TriangleList,
            PrimitiveRestartEnable = false
        };

        VkVertexInputBindingDescription vertexBinding = new((uint)Unsafe.SizeOf<SpriteVertex>());
        Span<VkVertexInputAttributeDescription> vertexAttributes = stackalloc VkVertexInputAttributeDescription[3];
        vertexAttributes[0] = new(location: 0, binding: 0, format: Vector3.AsFormat(), offset: (uint)Marshal.OffsetOf<SpriteVertex>(nameof(SpriteVertex.Position)));
        vertexAttributes[1] = new(location: 1, binding: 0, format: Vector4.AsFormat(), offset: (uint)Marshal.OffsetOf<SpriteVertex>(nameof(SpriteVertex.Color)));
        vertexAttributes[2] = new(location: 2, binding: 0, format: Vector2.AsFormat(), offset: (uint)Marshal.OffsetOf<SpriteVertex>(nameof(SpriteVertex.TexCoord)));

        _pipeline = new Pipeline(
            _device,
            _graphicsDevice.MainSwapchain.Format,
            _graphicsDevice.DepthImage.Format,
            pipelineDescription,
            _pipelineLayout,
            vertexBinding,
            vertexAttributes,
            "main"u8
        );

        Span<DescriptorPoolSize> poolSizes = stackalloc DescriptorPoolSize[1];
        poolSizes[0] = new DescriptorPoolSize(VkDescriptorType.CombinedImageSampler, GraphicsDevice.MAX_FRAMES_IN_FLIGHT); 
        _descriptorPool = new DescriptorPool(_device, poolSizes, GraphicsDevice.MAX_FRAMES_IN_FLIGHT);
        
        _descriptorSets = new DescriptorSet[GraphicsDevice.MAX_FRAMES_IN_FLIGHT];
        for (int i = 0; i < GraphicsDevice.MAX_FRAMES_IN_FLIGHT; i++) {
            _descriptorSets[i] = _descriptorPool.AllocateDescriptorSet(_descriptorSetLayout);
        }
        
        _spriteCapacity = 0;
        EnsureCapacity(_initialCapacity);
    }

    public SpriteBatchScope Begin(SpriteSortMode sortMode = SpriteSortMode.Deferred, Matrix4x4? transform = null) {
        if (_isActive) {
            throw new InvalidOperationException("SpriteBatch.Begin can only be called once per frame.");
        }

        _isActive = true;
        _spriteCount = 0;
        _sortMode = sortMode;
        _currentTexture = null;
        _commandBuffer = _graphicsDevice.RequestCurrentCommandBuffer();

        _transform = transform ?? CreateDefaultTransform();

        _commandBuffer.BindPipeline(_pipeline);
        Vector2 extent = new(_graphicsDevice.MainSwapchain.Width, _graphicsDevice.MainSwapchain.Height);
        _commandBuffer.SetViewport(new Rectangle(0f, 0f, extent.X, extent.Y));
        _commandBuffer.SetScissor(new Rectangle(0f, 0f, extent.X, extent.Y));
        PushTransform();

        return new SpriteBatchScope(this);
    }

    public void End() {
        if (!_isActive) {
            return;
        }

        Flush();
        _isActive = false;
        _currentTexture = null;
        _spriteCount = 0;
        _commandBuffer = default;
    }
    
    public void Draw(DrawSettings settings) {
        if (settings.Texture is null) {
            throw new ArgumentNullException(nameof(settings.Texture), "SpriteTexture must be provided in DrawSettings.");
        }

        Vector2 sourceSize = GetSourceSize(settings.Texture, settings.SourceRectangle);
        if (sourceSize.X <= 0f || sourceSize.Y <= 0f) {
            return;
        }

        Vector2 resolvedScale = settings.Scale == Vector2.Zero ? Vector2.One : settings.Scale;
        
        DrawInternal(
            settings.Texture, 
            settings.Position, 
            settings.SourceRectangle, 
            settings.Color, 
            settings.Rotation, 
            settings.Origin, 
            resolvedScale, 
            settings.Effects, 
            settings.LayerDepth
        );
    }

    public void Draw(
        SpriteTexture texture,
        Vector2 position,
        Rectangle? sourceRectangle,
        Color color,
        float rotation = 0f,
        Vector2 origin = default,
        Vector2 scale = default,
        SpriteEffects effects = SpriteEffects.None,
        float layerDepth = 0f)
    {
        Vector2 resolvedScale = scale == Vector2.Zero ? Vector2.One : scale;

        Draw(new DrawSettings {
            Texture = texture,
            Position = position,
            SourceRectangle = sourceRectangle,
            Color = color,
            Rotation = rotation,
            Origin = origin,
            Scale = resolvedScale,
            Effects = effects,
            LayerDepth = layerDepth
        });
    }

    public void Draw(
        SpriteTexture texture,
        Rectangle destinationRectangle,
        Color color,
        Rectangle? sourceRectangle = null,
        float rotation = 0f,
        Vector2 origin = default,
        SpriteEffects effects = SpriteEffects.None,
        float layerDepth = 0f)
    {
        Vector2 sourceSize = GetSourceSize(texture, sourceRectangle);
        if (sourceSize.X <= 0f || sourceSize.Y <= 0f) {
            return;
        }

        Vector2 calculatedScale = new(
            destinationRectangle.Width / sourceSize.X,
            destinationRectangle.Height / sourceSize.Y
        );
        
        Draw(new DrawSettings {
            Texture = texture,
            Position = destinationRectangle.Location,
            SourceRectangle = sourceRectangle,
            Color = color,
            Rotation = rotation,
            Origin = origin,
            Scale = calculatedScale,
            Effects = effects,
            LayerDepth = layerDepth
        });
    }

    private void DrawInternal( 
        SpriteTexture texture, 
        Vector2 position,
        Rectangle? sourceRectangle,
        Color color,
        float rotation,
        Vector2 origin,
        Vector2 scale, 
        SpriteEffects effects,
        float layerDepth)
    {
        if (!_isActive) {
            throw new InvalidOperationException("SpriteBatch.Draw must be called between Begin and End.");
        }
        if (texture is null) { 
            throw new ArgumentNullException(nameof(texture));
        }

        Vector2 sourceSize = GetSourceSize(texture, sourceRectangle);
        if (sourceSize.X <= 0f || sourceSize.Y <= 0f || scale.X == 0f || scale.Y == 0f) {
            return; 
        }

        EnsureCapacity(_spriteCount + 1);

        if (_currentTexture is not null && !ReferenceEquals(_currentTexture, texture)) {
            Flush();
            _currentTexture = null;
        }

        EnsureTextureBound(texture);

        Vector4 colorVec = color.ToVector4();

        float u0, v0, u1, v1;
        if (sourceRectangle.HasValue) {
            Rectangle src = sourceRectangle.Value;
            u0 = src.Left / texture.Width;
            v0 = src.Top / texture.Height;
            u1 = src.Right / texture.Width;
            v1 = src.Bottom / texture.Height;
        }
        else {
            u0 = 0f;
            v0 = 0f;
            u1 = 1f;
            v1 = 1f;
        }

        if ((effects & SpriteEffects.FlipHorizontally) != 0) {
            (u0, u1) = (u1, u0);
        }
        if ((effects & SpriteEffects.FlipVertically) != 0) {
            (v0, v1) = (v1, v0);
        }

        Vector2 scaledSize = sourceSize * scale;
        Vector2 originScaled = origin * scale;

        Vector2 topLeft = new(-originScaled.X, -originScaled.Y);
        Vector2 topRight = new(scaledSize.X - originScaled.X, -originScaled.Y);
        Vector2 bottomRight = new(scaledSize.X - originScaled.X, scaledSize.Y - originScaled.Y);
        Vector2 bottomLeft = new(-originScaled.X, scaledSize.Y - originScaled.Y);

        if (rotation != 0f) {
            float sin = MathF.Sin(rotation);
            float cos = MathF.Cos(rotation);

            topLeft = Rotate(topLeft, sin, cos);
            topRight = Rotate(topRight, sin, cos);
            bottomRight = Rotate(bottomRight, sin, cos);
            bottomLeft = Rotate(bottomLeft, sin, cos);
        }

        topLeft += position;
        topRight += position;
        bottomRight += position;
        bottomLeft += position;

        Vector2 texCoordTL = new(u0, v0);
        Vector2 texCoordTR = new(u1, v0);
        Vector2 texCoordBR = new(u1, v1);
        Vector2 texCoordBL = new(u0, v1);

        int vertexBase = _spriteCount * 4;
        _vertexScratch[vertexBase + 0] = new SpriteVertex(new Vector3(topLeft, layerDepth), color.ToVector4(), texCoordTL);
        _vertexScratch[vertexBase + 1] = new SpriteVertex(new Vector3(topRight, layerDepth), color.ToVector4(), texCoordTR);
        _vertexScratch[vertexBase + 2] = new SpriteVertex(new Vector3(bottomRight, layerDepth), color.ToVector4(), texCoordBR);
        _vertexScratch[vertexBase + 3] = new SpriteVertex(new Vector3(bottomLeft, layerDepth), color.ToVector4(), texCoordBL);

        _spriteCount++;
        if (_sortMode == SpriteSortMode.Immediate) {
            Flush();
        }
    }

    private void EnsureTextureBound(SpriteTexture texture) {
        if (_currentTexture is null) {
            _descriptorSets[_graphicsDevice.CurrentFrameIndex].Update(
                texture.ImageView, texture.Sampler, VkDescriptorType.CombinedImageSampler);
            _currentTexture = texture;
        }
    }

    private void EnsureCapacity(int requiredSpriteCount) {
        if (requiredSpriteCount <= _spriteCapacity) {
            return;
        }

        int newCapacity = _spriteCapacity == 0 ? _initialCapacity : _spriteCapacity;
        while (newCapacity < requiredSpriteCount) {
            newCapacity *= 2;
        }

        _spriteCapacity = newCapacity;

        int vertexCount = _spriteCapacity * 4;
        int indexCount = _spriteCapacity * 6;

        Array.Resize(ref _vertexScratch, vertexCount);
        Array.Resize(ref _indexScratch, indexCount);

        uint newVertexBufferSize = (uint)(vertexCount * Unsafe.SizeOf<SpriteVertex>());
        uint newIndexBufferSize = (uint)(indexCount * sizeof(uint));

        if (!_buffersInitialized) {
            _vertexBuffer = new VertexBuffer<SpriteVertex>(_device, newVertexBufferSize, VkBufferUsageFlags.VertexBuffer);
            _indexBuffer = new IndexBuffer(_device, newIndexBufferSize, VkBufferUsageFlags.IndexBuffer);
            _buffersInitialized = true;
        }
        else
        {
            _vertexBuffer.Resize(_device, newVertexBufferSize, VkBufferUsageFlags.VertexBuffer);
            _indexBuffer.Resize(_device, newIndexBufferSize, VkBufferUsageFlags.IndexBuffer);
        }

        GenerateIndices(_spriteCapacity); 
        _indexBuffer.CopyFrom(new ReadOnlySpan<uint>(_indexScratch, 0, indexCount));
    }

    private void GenerateIndices(int spriteCapacity) {
        for (int i = 0; i < spriteCapacity; i++) {
            int vertexStart = i * 4;
            int indexStart = i * 6;

            _indexScratch[indexStart + 0] = (uint)vertexStart;
            _indexScratch[indexStart + 1] = (uint)(vertexStart + 1);
            _indexScratch[indexStart + 2] = (uint)(vertexStart + 2);
            _indexScratch[indexStart + 3] = (uint)(vertexStart + 2);
            _indexScratch[indexStart + 4] = (uint)(vertexStart + 3);
            _indexScratch[indexStart + 5] = (uint)vertexStart;
        }
    }
    
    private unsafe void Flush() {
        if (_spriteCount == 0 || _currentTexture is null) {
            _spriteCount = 0;
            return;
        }

        int vertexCount = _spriteCount * 4;
        int indexCount = _spriteCount * 6;

        _vertexBuffer.CopyFrom(new ReadOnlySpan<SpriteVertex>(_vertexScratch, 0, vertexCount));

        VkBuffer vertexBufferHandle = _vertexBuffer.Buffer.Value;
        ulong offset = 0;
        vkCmdBindVertexBuffers(_commandBuffer, 0, 1, &vertexBufferHandle, &offset);
        vkCmdBindIndexBuffer(_commandBuffer, _indexBuffer.Buffer.Value, 0, VkIndexType.Uint32);

        var currentFrameDescriptorSet = _descriptorSets[_graphicsDevice.CurrentFrameIndex];
        Span<DescriptorSet> descriptorSetsToBind = stackalloc DescriptorSet[1];
        descriptorSetsToBind[0] = currentFrameDescriptorSet;
        _commandBuffer.BindDescriptorSets(_pipelineLayout, descriptorSetsToBind);

        vkCmdDrawIndexed(_commandBuffer, (uint)indexCount, 1, 0, 0, 0);

        _spriteCount = 0;
    }

    private unsafe void PushTransform() {
        Matrix4x4 transform = _transform;
        vkCmdPushConstants(
            _commandBuffer,
            _pipelineLayout.Value, 
            VkShaderStageFlags.Vertex,
            0,
            (uint)Unsafe.SizeOf<Matrix4x4>(),
            &transform
        );
    }

    private Matrix4x4 CreateDefaultTransform() {
        Vector2 extent = new(_graphicsDevice.MainSwapchain.Width, _graphicsDevice.MainSwapchain.Height);
        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(0f, extent.X, 0f, extent.Y, -1f, 1f);
        return projection;
    }

    private static Vector2 GetSourceSize(SpriteTexture texture, Rectangle? sourceRectangle) {
        if (sourceRectangle.HasValue) {
            Rectangle src = sourceRectangle.Value;
            return new Vector2(src.Width, src.Height);
        }

        return new Vector2(texture.Width, texture.Height);
    }

    private static Vector2 Rotate(Vector2 value, float sin, float cos) {
        return new Vector2(
            value.X * cos - value.Y * sin,
            value.X * sin + value.Y * cos
        );
    }

    public void Dispose() {
        GC.SuppressFinalize(this);

        if (_isActive) {
            Flush();
            _isActive = false;
        }

        foreach (var ds in _descriptorSets) {
            ds.Dispose();
        }

        _descriptorPool.Dispose();
        _pipeline.Dispose();
        _pipelineLayout.Dispose();
        _descriptorSetLayout.Dispose();

        if (_buffersInitialized) {
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
            _buffersInitialized = false;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SpriteVertex(Vector3 position, Vector4 color, Vector2 texCoord) {
        public Vector3 Position = position;
        public Vector4 Color = color;
        public Vector2 TexCoord = texCoord;
    }
}

public readonly struct SpriteBatchScope : IDisposable {
    private readonly SpriteBatch _batch;

    internal SpriteBatchScope(SpriteBatch batch) {
        _batch = batch;
    }

    public void Draw(SpriteBatch.DrawSettings settings) {
        _batch.Draw(settings);
    }

    public void Draw(
        SpriteTexture texture,
        Vector2 position,
        Rectangle? sourceRectangle,
        Color color,
        float rotation = 0f,
        Vector2 origin = default,
        Vector2 scale = default,
        SpriteEffects effects = SpriteEffects.None,
        float layerDepth = 0f)
    {
        _batch.Draw(texture, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);
    }

    public void Draw(
        SpriteTexture texture,
        Rectangle destinationRectangle,
        Color color,
        Rectangle? sourceRectangle = null,
        float rotation = 0f,
        Vector2 origin = default,
        SpriteEffects effects = SpriteEffects.None,
        float layerDepth = 0f)
    {
        _batch.Draw(texture, destinationRectangle, color, sourceRectangle, rotation, origin, effects, layerDepth);
    }

    public void Dispose() {
        _batch.End();
    }
}