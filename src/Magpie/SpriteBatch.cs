using System;
using System.Collections.Generic;
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
    private DescriptorSet _currentBatchDescriptorSet; 
    private byte[] _vertexShaderCode;
    private byte[] _fragmentShaderCode;
    private bool _pipelineInitialized;

    private readonly List<BatchBuffer> _buffers = new();
    private readonly List<DescriptorSet>[] _descriptorSetsInFlight;
    private int _currentBufferIndex;
    private int _lastFrameIndex = -1;
    private readonly Queue<RetiredPipeline> _retiredPipelines = new();

    private SpriteVertex[] _vertexScratch = Array.Empty<SpriteVertex>();
    private uint[] _indexScratch = Array.Empty<uint>();

    private int _scratchCapacity;
    private int _spriteCount;
    private SpriteTexture? _currentTexture;
    private bool _isActive;
    private SpriteSortMode _sortMode;
    private Matrix4x4 _transform;
    private CmdBuffer _commandBuffer;

    public struct DrawSettings {
        public required SpriteTexture Texture;
        public required Color Color;
        
        public Vector2 Position;
        public Rectangle? SourceRectangle;
        public float Rotation;
        public Vector2 Origin;
        public Vector2 Scale;
        public SpriteEffects Effects;
        public float LayerDepth;
    }

    private sealed class BatchBuffer : IDisposable {
        public VertexBuffer<SpriteVertex> VertexBuffer;
        public IndexBuffer IndexBuffer;
        public readonly int SpriteCapacity;
        public uint VertexCursor;
        public uint IndexCursor;

        public BatchBuffer(VertexBuffer<SpriteVertex> vertexBuffer, IndexBuffer indexBuffer, int spriteCapacity) {
            VertexBuffer = vertexBuffer;
            IndexBuffer = indexBuffer;
            SpriteCapacity = spriteCapacity;
            VertexCursor = 0;
            IndexCursor = 0;
        }

        public void Reset() {
            VertexCursor = 0;
            IndexCursor = 0;
        }

        public void Dispose() {
            VertexBuffer.Dispose();
            IndexBuffer.Dispose();
        }
    }

    private BatchBuffer CurrentBuffer => _buffers[_currentBufferIndex];

    private readonly struct RetiredPipeline {
        public readonly Pipeline Pipeline;
        public readonly int FrameIndex;

        public RetiredPipeline(Pipeline pipeline, int frameIndex) {
            Pipeline = pipeline;
            FrameIndex = frameIndex;
        }
    }

    private BatchBuffer CreateBuffer(int spriteCapacity) {
        int resolvedCapacity = Math.Max(1, spriteCapacity);
        uint vertexBufferSize = (uint)(resolvedCapacity * 4 * Unsafe.SizeOf<SpriteVertex>());
        uint indexBufferSize = (uint)(resolvedCapacity * 6 * sizeof(uint));

        var vertexBuffer = new VertexBuffer<SpriteVertex>(_device, vertexBufferSize);
        var indexBuffer = new IndexBuffer(_device, indexBufferSize);
        return new BatchBuffer(vertexBuffer, indexBuffer, resolvedCapacity);
    }

    private void RetirePipeline(Pipeline pipeline) {
        _retiredPipelines.Enqueue(new RetiredPipeline(pipeline, _graphicsDevice.CurrentFrameIndex));
    }

    private void ReleaseRetiredPipelines(int frameIndex) {
        while (_retiredPipelines.Count > 0) {
            RetiredPipeline retired = _retiredPipelines.Peek();
            if (retired.FrameIndex != frameIndex) {
                break;
            }

            _retiredPipelines.Dequeue();
            retired.Pipeline.Dispose();
        }
    }

    private void EnsureScratchCapacity(int spriteCapacity) {
        if (spriteCapacity <= _scratchCapacity) {
            return;
        }

        int oldCapacity = _scratchCapacity;
        _scratchCapacity = spriteCapacity;

        int vertexCount = _scratchCapacity * 4;
        int indexCount = _scratchCapacity * 6;

        Array.Resize(ref _vertexScratch, vertexCount);
        Array.Resize(ref _indexScratch, indexCount);

        GenerateIndices(oldCapacity, _scratchCapacity);
    }

    private void RebuildPipeline() {
        if (_pipelineInitialized) {
            RetirePipeline(_pipeline);
        }

        using ShaderModule vertexModule = new(_device, _vertexShaderCode);
        using ShaderModule fragmentModule = new(_device, _fragmentShaderCode);

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

        _pipelineInitialized = true;
    }

    private void EnsureSpaceForSprites(int spritesNeeded) {
        while (true) {
            var buffer = CurrentBuffer;
            int committedSprites = (int)(buffer.VertexCursor / 4);
            int remainingSprites = buffer.SpriteCapacity - committedSprites - _spriteCount;

            if (spritesNeeded <= remainingSprites) {
                EnsureScratchCapacity(buffer.SpriteCapacity);
                return;
            }

            Flush();

            buffer = CurrentBuffer;
            committedSprites = (int)(buffer.VertexCursor / 4);
            remainingSprites = buffer.SpriteCapacity - committedSprites - _spriteCount;

            if (spritesNeeded <= remainingSprites) {
                EnsureScratchCapacity(buffer.SpriteCapacity);
                return;
            }

            MoveToNextBuffer(Math.Max(buffer.SpriteCapacity * 2, Math.Max(spritesNeeded, _initialCapacity)));
        }
    }

    private void MoveToNextBuffer(int minimumSpriteCapacity) {
        if (_currentBufferIndex + 1 < _buffers.Count) {
            _currentBufferIndex++;
        }
        else {
            int capacity = Math.Max(minimumSpriteCapacity, CurrentBuffer.SpriteCapacity * 2);
            var newBuffer = CreateBuffer(capacity);
            _buffers.Add(newBuffer);
            _currentBufferIndex = _buffers.Count - 1;
        }

        EnsureScratchCapacity(CurrentBuffer.SpriteCapacity);
    }

    private void ResetBuffersForFrame() {
        int frameIndex = _graphicsDevice.CurrentFrameIndex;
        ReleaseRetiredPipelines(frameIndex);

        var descriptorSets = _descriptorSetsInFlight[frameIndex];
        foreach (var descriptorSet in descriptorSets) {
            if (descriptorSet.Value != VkDescriptorSet.Null) {
                descriptorSet.Dispose();
            }
        }
        descriptorSets.Clear();

        foreach (var buffer in _buffers) {
            buffer.Reset();
        }

        _currentBufferIndex = 0;
        _currentTexture = null;
        _currentBatchDescriptorSet = default;
        _spriteCount = 0;
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

        _descriptorSetsInFlight = new List<DescriptorSet>[GraphicsDevice.MAX_FRAMES_IN_FLIGHT];
        for (int i = 0; i < _descriptorSetsInFlight.Length; i++) {
            _descriptorSetsInFlight[i] = new List<DescriptorSet>();
        }

        _vertexShaderCode = vertexShaderCode.ToArray();
        _fragmentShaderCode = fragmentShaderCode.ToArray();

        _descriptorSetLayout = new DescriptorSetLayout(_device, 0, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment);

        VkPushConstantRange pushConstantRange = new() {
            stageFlags = VkShaderStageFlags.Vertex,
            offset = 0,
            size = (uint)Unsafe.SizeOf<Matrix4x4>()
        };

        PushConstant pushConstant = new(pushConstantRange.offset, pushConstantRange.size, pushConstantRange.stageFlags);
        _pipelineLayout = new PipelineLayout(_device, [_descriptorSetLayout], [pushConstant]);
        RebuildPipeline();

        Span<DescriptorPoolSize> poolSizes = stackalloc DescriptorPoolSize[1];
        poolSizes[0] = new DescriptorPoolSize(VkDescriptorType.CombinedImageSampler, 256);
        _descriptorPool = new DescriptorPool(_device, poolSizes, 256);

        var initialBuffer = CreateBuffer(_initialCapacity);
        _buffers.Add(initialBuffer);
        _currentBufferIndex = 0;
        EnsureScratchCapacity(initialBuffer.SpriteCapacity);
    }

    public void SwapShaders(ReadOnlySpan<byte> vertexShaderCode, ReadOnlySpan<byte> fragmentShaderCode) {
        if (vertexShaderCode.IsEmpty) {
            throw new ArgumentException("Vertex shader code cannot be empty.", nameof(vertexShaderCode));
        }
        if (fragmentShaderCode.IsEmpty) {
            throw new ArgumentException("Fragment shader code cannot be empty.", nameof(fragmentShaderCode));
        }
        if (_isActive) {
            throw new InvalidOperationException("Cannot swap shaders while SpriteBatch is active.");
        }

        _vertexShaderCode = vertexShaderCode.ToArray();
        _fragmentShaderCode = fragmentShaderCode.ToArray();
        RebuildPipeline();
    }

    public SpriteBatchScope Begin(SpriteSortMode sortMode = SpriteSortMode.Deferred, Matrix4x4? transform = null) {
        int frameIndex = _graphicsDevice.CurrentFrameIndex;
        if (_lastFrameIndex != frameIndex) {
            ResetBuffersForFrame();
            _lastFrameIndex = frameIndex;
        }

        if (_isActive) {
            throw new InvalidOperationException("SpriteBatch.Begin can only be called once per frame.");
        }

        _isActive = true;
        _spriteCount = 0;
        _sortMode = sortMode;
        _currentTexture = null;
        _currentBatchDescriptorSet = default;
        _commandBuffer = _graphicsDevice.RequestCurrentCommandBuffer();

        _transform = transform ?? CreateDefaultTransform();

        _commandBuffer.BindPipeline(_pipeline);
        float viewportWidth = Math.Max(1u, _graphicsDevice.CurrentRenderWidth);
        float viewportHeight = Math.Max(1u, _graphicsDevice.CurrentRenderHeight);
        _commandBuffer.SetViewport(new Rectangle(0f, 0f, viewportWidth, viewportHeight));
        _commandBuffer.SetScissor(new Rectangle(0f, 0f, viewportWidth, viewportHeight));
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
        _currentBatchDescriptorSet = default;
        _spriteCount = 0;
        _commandBuffer = default;
    }
    
    public void Draw(DrawSettings settings) {
        Vector2 sourceSize = GetSourceSize(settings.Texture, settings.SourceRectangle);
        if (sourceSize.X <= 0f || sourceSize.Y <= 0f) {
            return;
        }

        Vector2 finalScale = settings.Scale == Vector2.Zero ? Vector2.One : settings.Scale;
        
        DrawInternal(
            settings.Texture, 
            settings.Position, 
            settings.SourceRectangle, 
            settings.Color, 
            settings.Rotation, 
            settings.Origin, 
            finalScale, 
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

        EnsureSpaceForSprites(1);

        if (_currentTexture is not null && !ReferenceEquals(_currentTexture, texture)) {
            Flush();
            _currentTexture = null;
            _currentBatchDescriptorSet = default;
        }

        EnsureTextureBound(texture);

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

        Vector4 colorVec = color.ToVector4();
        int vertexBase = _spriteCount * 4;
        _vertexScratch[vertexBase + 0] = new SpriteVertex(new Vector3(topLeft, layerDepth), colorVec, texCoordTL);
        _vertexScratch[vertexBase + 1] = new SpriteVertex(new Vector3(topRight, layerDepth), colorVec, texCoordTR);
        _vertexScratch[vertexBase + 2] = new SpriteVertex(new Vector3(bottomRight, layerDepth), colorVec, texCoordBR);
        _vertexScratch[vertexBase + 3] = new SpriteVertex(new Vector3(bottomLeft, layerDepth), colorVec, texCoordBL);

        _spriteCount++;
        if (_sortMode == SpriteSortMode.Immediate) {
            Flush();
        }
    }

    private void EnsureTextureBound(SpriteTexture texture) {
        if (_currentBatchDescriptorSet.Value != VkDescriptorSet.Null && ReferenceEquals(_currentTexture, texture)) {
            return;
        }

        var descriptorSet = _descriptorPool.AllocateDescriptorSet(_descriptorSetLayout);
        descriptorSet.Update(texture.ImageView, texture.Sampler, VkDescriptorType.CombinedImageSampler);

        _currentBatchDescriptorSet = descriptorSet;
        _currentTexture = texture;

        int frameIndex = _graphicsDevice.CurrentFrameIndex;
        _descriptorSetsInFlight[frameIndex].Add(descriptorSet);
    }

    private unsafe void GenerateIndices(int start, int end) {
        fixed (uint* indexBase = _indexScratch) {
            uint* ptr = indexBase + (start * 6);
            
            int count = end - start;
            int i = start;
            
            while (count >= 4) {
                uint vStart0 = (uint)(i * 4);
                uint vStart1 = (uint)((i + 1) * 4);
                uint vStart2 = (uint)((i + 2) * 4);
                uint vStart3 = (uint)((i + 3) * 4);

                ptr[0] = vStart0;
                ptr[1] = vStart0 + 1;
                ptr[2] = vStart0 + 2;
                ptr[3] = vStart0 + 2;
                ptr[4] = vStart0 + 3;
                ptr[5] = vStart0;

                ptr[6] = vStart1;
                ptr[7] = vStart1 + 1;
                ptr[8] = vStart1 + 2;
                ptr[9] = vStart1 + 2;
                ptr[10] = vStart1 + 3;
                ptr[11] = vStart1;

                ptr[12] = vStart2;
                ptr[13] = vStart2 + 1;
                ptr[14] = vStart2 + 2;
                ptr[15] = vStart2 + 2;
                ptr[16] = vStart2 + 3;
                ptr[17] = vStart2;

                ptr[18] = vStart3;
                ptr[19] = vStart3 + 1;
                ptr[20] = vStart3 + 2;
                ptr[21] = vStart3 + 2;
                ptr[22] = vStart3 + 3;
                ptr[23] = vStart3;

                ptr += 24;
                i += 4;
                count -= 4;
            }

            while (count > 0) {
                uint vertexStart = (uint)(i * 4);

                ptr[0] = vertexStart;
                ptr[1] = vertexStart + 1;
                ptr[2] = vertexStart + 2;
                ptr[3] = vertexStart + 2;
                ptr[4] = vertexStart + 3;
                ptr[5] = vertexStart;

                ptr += 6;
                i++;
                count--;
            }
        }
    }
    
    private unsafe void Flush() {
        if (_spriteCount == 0) {
            return;
        }

        if (_currentTexture is null || _currentBatchDescriptorSet.Value == VkDescriptorSet.Null) {
            _spriteCount = 0;
            return;
        }

        BatchBuffer buffer = CurrentBuffer;

        int vertexCount = _spriteCount * 4;
        int indexCount = _spriteCount * 6;

        uint vertexCapacity = (uint)(buffer.SpriteCapacity * 4);
        uint indexCapacity = (uint)(buffer.SpriteCapacity * 6);

        if (buffer.VertexCursor + (uint)vertexCount > vertexCapacity || buffer.IndexCursor + (uint)indexCount > indexCapacity) {
            MoveToNextBuffer(Math.Max(buffer.SpriteCapacity * 2, _spriteCount));
            buffer = CurrentBuffer;
        }

        uint vertexStart = buffer.VertexCursor;
        uint indexStart = buffer.IndexCursor;

        buffer.VertexBuffer.CopyFrom(new ReadOnlySpan<SpriteVertex>(_vertexScratch, 0, vertexCount), vertexStart);

        buffer.IndexBuffer.CopyFrom(new ReadOnlySpan<uint>(_indexScratch, 0, indexCount), indexStart);

        VkBuffer vertexBufferHandle = buffer.VertexBuffer.Buffer.Value;
        ulong vertexOffset = 0;
        vkCmdBindVertexBuffers(_commandBuffer, 0, 1, &vertexBufferHandle, &vertexOffset);
        vkCmdBindIndexBuffer(_commandBuffer, buffer.IndexBuffer.Buffer.Value, 0, VkIndexType.Uint32);

        Span<DescriptorSet> descriptorSetsToBind = stackalloc DescriptorSet[1];
        descriptorSetsToBind[0] = _currentBatchDescriptorSet;
        _commandBuffer.BindDescriptorSets(_pipelineLayout, descriptorSetsToBind);

        vkCmdDrawIndexed(_commandBuffer, (uint)indexCount, 1, indexStart, (int)vertexStart, 0);

        buffer.VertexCursor += (uint)vertexCount;
        buffer.IndexCursor += (uint)indexCount;

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
        float width = Math.Max(1u, _graphicsDevice.CurrentRenderWidth);
        float height = Math.Max(1u, _graphicsDevice.CurrentRenderHeight);
        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(0f, width, 0f, height, -1f, 1f);
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
        
        if (_currentBatchDescriptorSet.Value != VkDescriptorSet.Null) {
            _currentBatchDescriptorSet.Dispose();
            _currentBatchDescriptorSet = default;
        }

        foreach (var descriptorSets in _descriptorSetsInFlight) {
            foreach (var descriptorSet in descriptorSets) {
                if (descriptorSet.Value != VkDescriptorSet.Null) {
                    descriptorSet.Dispose();
                }
            }
            descriptorSets.Clear();
        }

        while (_retiredPipelines.Count > 0) {
            var retired = _retiredPipelines.Dequeue();
            retired.Pipeline.Dispose();
        }

        _descriptorPool.Dispose();
        if (_pipelineInitialized) {
            _pipeline.Dispose();
            _pipelineInitialized = false;
        }
        _pipelineLayout.Dispose();
        _descriptorSetLayout.Dispose();

        foreach (var buffer in _buffers) {
            buffer.Dispose();
        }
        _buffers.Clear();
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