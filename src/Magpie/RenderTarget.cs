using System;
using Magpie.Core;
using Magpie.Utilities;
using Standard;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie;

public sealed class RenderTarget : IDisposable {
    private readonly GraphicsDevice _graphicsDevice;
    private readonly LogicalDevice _device;
    private readonly Image _image;
    private readonly DeviceMemory _memory;
    private readonly ImageView _imageView;
    private readonly Sampler _sampler;
    private readonly SpriteTexture _spriteTextureView;

    private VkImageLayout _currentLayout = VkImageLayout.Undefined;
    private bool _isRendering;
    private bool _disposed;

    public uint Width { get; }
    public uint Height { get; }
    public VkFormat Format { get; }

    public SpriteTexture SpriteTextureView => _spriteTextureView;
    internal ref readonly Image Image => ref _image;
    internal VkImageLayout CurrentLayout => _currentLayout;

    public RenderTarget(GraphicsDevice graphicsDevice, uint width, uint height, VkFormat format = VkFormat.R8G8B8A8Unorm, VkFilter filter = VkFilter.Linear, VkSamplerAddressMode addressMode = VkSamplerAddressMode.ClampToEdge) {
        if (graphicsDevice is null) {
            throw new ArgumentNullException(nameof(graphicsDevice));
        }
        if (width == 0 || height == 0) {
            throw new ArgumentOutOfRangeException(nameof(width), "Render targets must be at least 1x1.");
        }

        _graphicsDevice = graphicsDevice;
        _device = graphicsDevice.LogicalDevice;
        Width = width;
        Height = height;
        Format = format;

        VkImageUsageFlags usage = VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled | VkImageUsageFlags.TransferSrc | VkImageUsageFlags.TransferDst;
        _image = new Image(_device, width, height, 1, format, usage);
        _memory = new DeviceMemory(_image, VkMemoryPropertyFlags.DeviceLocal);
        _imageView = new ImageView(_image);
        _sampler = new Sampler(_device, new SamplerCreateParameters(filter, addressMode));
        _spriteTextureView = new SpriteTexture(_image, _memory, _imageView, _sampler, ownsResources: false);
    }

    public RenderTargetScope BeginScope(GraphicsDevice device, Color? clearColor = null, VkAttachmentLoadOp loadOp = VkAttachmentLoadOp.Clear, VkImageLayout finalLayout = VkImageLayout.ShaderReadOnlyOptimal) {
        if (device is null) {
            throw new ArgumentNullException(nameof(device));
        }
        if (!device.IsFrameStarted) {
            throw new InvalidOperationException("BeginScope requires an active frame. Call GraphicsDevice.BeginFrame first.");
        }
        if (!ReferenceEquals(device, _graphicsDevice)) {
            throw new InvalidOperationException("RenderTarget must be used with the GraphicsDevice it was created with.");
        }
        if (_isRendering) {
            throw new InvalidOperationException("RenderTarget scopes cannot be nested yet!."); // todo: fix
        }

        VkClearValue clearValue = clearColor?.ToVkClearValue() ?? new VkClearValue { color = new VkClearColorValue(0f, 0f, 0f, 0f) };
        _isRendering = true;
        return new RenderTargetScope(device, this, clearValue, loadOp, finalLayout);
    }

    internal void CompleteScope(VkImageLayout newLayout) {
        _currentLayout = newLayout;
        _isRendering = false;
    }

    internal void TransitionTo(CmdBuffer commandBuffer, VkImageLayout targetLayout) {
        if (_currentLayout == targetLayout) {
            return;
        }

        commandBuffer.TransitionImageLayout(_image, _currentLayout, targetLayout);
        _currentLayout = targetLayout;
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _spriteTextureView.Dispose();
        _sampler.Dispose();
        _imageView.Dispose();
        _memory.Dispose();
        _image.Dispose();
        _disposed = true;
    }
}

public struct RenderTargetScope : IDisposable {
    private readonly RenderTarget _target;
    private readonly CmdBuffer _commandBuffer;
    private readonly VkImageLayout _finalLayout;
    private bool _disposed;

    internal unsafe RenderTargetScope(GraphicsDevice device, RenderTarget target, VkClearValue clearValue, VkAttachmentLoadOp loadOp, VkImageLayout finalLayout) {
        _target = target;
        _commandBuffer = device.RequestCurrentCommandBuffer();
        _finalLayout = finalLayout;

        target.TransitionTo(_commandBuffer, VkImageLayout.ColorAttachmentOptimal);

        VkRenderingAttachmentInfo colorAttachment = new() {
            sType = VkStructureType.RenderingAttachmentInfo,
            imageView = target.SpriteTextureView.ImageView.Value,
            imageLayout = VkImageLayout.ColorAttachmentOptimal,
            loadOp = loadOp,
            storeOp = VkAttachmentStoreOp.Store,
            clearValue = clearValue
        };

        VkOffset2D offset = new VkOffset2D(0, 0);
        VkExtent2D extent = new VkExtent2D(target.Width, target.Height);
        VkRect2D renderArea = new VkRect2D(offset, extent);

        VkRenderingInfo renderingInfo = new() {
            sType = VkStructureType.RenderingInfo,
            renderArea = renderArea,
            layerCount = 1,
            colorAttachmentCount = 1,
            pColorAttachments = &colorAttachment
        };

        vkCmdBeginRendering(_commandBuffer, &renderingInfo);
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        vkCmdEndRendering(_commandBuffer);
        _target.TransitionTo(_commandBuffer, _finalLayout);
        _target.CompleteScope(_finalLayout);
        _disposed = true;
    }
}
