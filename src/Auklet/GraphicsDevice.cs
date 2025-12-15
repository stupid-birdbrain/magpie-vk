using System.Collections.Generic;
using System.Runtime.InteropServices;
using Auklet.Core;
using Magpie.Utilities;
using SDL3;
using Standard;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using Image = Auklet.Core.Image;
using Semaphore = Auklet.Core.Semaphore;

namespace Auklet;

public sealed unsafe class GraphicsDevice : IDisposable {
    public const int MAX_FRAMES_IN_FLIGHT = 2;

    public VulkanInstance Instance;
    private Swapchain _mainSwapchain;
    private Surface _surface;
    private readonly CmdPool _graphicsCmdPool;
    private readonly CmdBuffer[] _mainCommandBuffers; //one buffer per frame in flight

    private readonly Queue _presentQueue;
    private readonly Queue _graphicsQueue;

    private readonly PhysicalDevice _physicalDevice;
    private readonly LogicalDevice _logicalDevice;

    private readonly Semaphore[] _imageAvailableSemaphores;
    private Semaphore[] _renderFinishedSemaphores;
    private readonly Fence[] _inFlightFences;
    private VkFence[] _imagesInFlight = null!;
    private DepthImage _depthImage;
    
    private bool _frameBufferResized = false;

    private readonly FencePool _fences;

    private uint _imageIndex;
    private bool _isFrameStarted;
    private int _currentFrame;
    private readonly Stack<RenderingScope> _renderScopeStack = new();
    private RenderingScope _currentScope;

    public bool IsFrameStarted => _isFrameStarted;
    public Swapchain MainSwapchain => _mainSwapchain;
    
    public CmdPool GraphicsCommandPool => _graphicsCmdPool;
    public Queue GraphicsQueue => _graphicsQueue;
    public int CurrentFrameIndex => _currentFrame;
    public LogicalDevice LogicalDevice => _logicalDevice;
    public DepthImage DepthImage => _depthImage;
    public uint CurrentRenderWidth { get; private set; }
    public uint CurrentRenderHeight { get; private set; }
    

    public GraphicsDevice(VulkanInstance instance, Surface surface, PhysicalDevice physicalDevice, LogicalDevice logicalDevice) {
        Instance = instance;
        _surface = surface;
        _physicalDevice = physicalDevice;
        _logicalDevice = logicalDevice;
        
        var queueFamilies = _physicalDevice.FindQueueFamilies(_surface);
        _graphicsQueue = _logicalDevice.GetQueue(queueFamilies.GraphicsFamily!.Value, 0);
        _presentQueue = _logicalDevice.GetQueue(queueFamilies.PresentFamily!.Value, 0);
        
        CreateSwapchain();

        _graphicsCmdPool = new(_logicalDevice, _graphicsQueue);
        Console.WriteLine($"main cmd pool created!");

        _mainCommandBuffers = new CmdBuffer[MAX_FRAMES_IN_FLIGHT];
        _imageAvailableSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
        _inFlightFences = new Fence[MAX_FRAMES_IN_FLIGHT];

        for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++) {
            _mainCommandBuffers[i] = _graphicsCmdPool.CreateCommandBuffer();
            _imageAvailableSemaphores[i] = new(_logicalDevice);
            _inFlightFences[i] = new(_logicalDevice);
        }
        
        _renderFinishedSemaphores = new Semaphore[_mainSwapchain.Images.Length];
        for (int i = 0; i < _mainSwapchain.Images.Length; i++) {
             _renderFinishedSemaphores[i] = new(_logicalDevice);
        }

        _fences = new FencePool(_logicalDevice);
        
        CreateDepthResources();
    }
    
    public void NotifyResize() {
        _frameBufferResized = true;
    }
    
    public FenceLease RequestFence(VkFenceCreateFlags flags) => _fences.Rent(flags);
    
    /// <summary>
    ///     Attempts to begin a new rendering frame and clears the backbuffer.
    /// </summary>
    public bool Begin(VkClearValue clearColor) {
        if (_isFrameStarted) {
            throw new InvalidOperationException("cannot call Begin twice in a frame! Call End() first.");
        }

        Fence currentInFlightFence = _inFlightFences[_currentFrame];
        CmdBuffer currentCmdBuffer = _mainCommandBuffers[_currentFrame];
        Semaphore currentImageAvailableSemaphore = _imageAvailableSemaphores[_currentFrame];

        currentInFlightFence.Wait();
        
        VkResult result = vkAcquireNextImageKHR(
            _logicalDevice,
            _mainSwapchain.Value,
            ulong.MaxValue,
            currentImageAvailableSemaphore,
            VkFence.Null,
            out _imageIndex
        );

        if (result == VkResult.ErrorOutOfDateKHR) {
            RecreateSwapchain();
            _isFrameStarted = false;
            return false;
        }
        else if (result != VkResult.Success && result != VkResult.SuboptimalKHR) {
            throw new Exception($"failed to acquire swapchain image!: {result}");
        }
        
        if (_mainSwapchain.Images.Length == 0) {
            RecreateSwapchain();
            _isFrameStarted = false;
            return false;
        }
        if (_imageIndex >= _mainSwapchain.Images.Length) {
            RecreateSwapchain();
            _isFrameStarted = false;
            return false;
        }

        VkFence imageFence = _imagesInFlight[(int)_imageIndex];
        if (imageFence.Handle != VkFence.Null.Handle)
        {
            vkWaitForFences(_logicalDevice, 1, &imageFence, true, ulong.MaxValue).CheckResult();
        }

        _imagesInFlight[(int)_imageIndex] = currentInFlightFence;

        _isFrameStarted = true;
        currentInFlightFence.Reset();

        currentCmdBuffer.Reset();
        currentCmdBuffer.Begin(flags: VkCommandBufferUsageFlags.OneTimeSubmit);

        Image swapchainImage = new(_logicalDevice, _mainSwapchain.Images[_imageIndex], _mainSwapchain.Width, _mainSwapchain.Height, _mainSwapchain.Format);
        currentCmdBuffer.TransitionImageLayout(swapchainImage, VkImageLayout.Undefined, VkImageLayout.ColorAttachmentOptimal, aspects: VkImageAspectFlags.Color);
        currentCmdBuffer.TransitionImageLayout(_depthImage.Image, VkImageLayout.Undefined, VkImageLayout.DepthStencilAttachmentOptimal, aspects: VkImageAspectFlags.Depth);

        BeginSwapchainRendering(currentCmdBuffer, VkAttachmentLoadOp.Clear, VkAttachmentLoadOp.Clear, clearColor);

        _renderScopeStack.Clear();
        _currentScope = new RenderingScope {
            Type = RenderingScopeType.Swapchain,
            SwapchainClearValue = clearColor,
            Width = _mainSwapchain.Width,
            Height = _mainSwapchain.Height
        };
        CurrentRenderWidth = _currentScope.Width;
        CurrentRenderHeight = _currentScope.Height;
        return true;
    }
    
    public bool Begin(Color color) => Begin(color.ToVkClearValue());
    
    public CmdBuffer AllocateCommandBuffer(bool level) {
        return _graphicsCmdPool.CreateCommandBuffer(level);
    }
    
    public CmdBuffer RequestCurrentCommandBuffer() {
        if (!_isFrameStarted) {
            throw new InvalidOperationException("cannot get command buffer! a frame was not successfully started or has already ended!");
        }
        
        return _mainCommandBuffers[_currentFrame];
    }

    public RenderTarget CreateRenderTarget(uint width, uint height, VkFormat? format = null) {
        VkFormat targetFormat = format ?? _mainSwapchain.Format;
        return new RenderTarget(_logicalDevice, _graphicsCmdPool, _graphicsQueue, width, height, targetFormat);
    }

    public RenderTargetScope PushRenderTarget(RenderTarget renderTarget, VkClearValue clearValue) {
        Span<VkClearValue> clearValues = stackalloc VkClearValue[1];
        clearValues[0] = clearValue;
        ReadOnlySpan<RenderTarget> targets = MemoryMarshal.CreateReadOnlySpan(ref renderTarget, 1);
        return PushRenderTargets(targets, clearValues);
    }

    public RenderTargetScope PushRenderTargets(ReadOnlySpan<RenderTarget> renderTargets, ReadOnlySpan<VkClearValue> clearValues) {
        if (!_isFrameStarted) {
            throw new InvalidOperationException("cannot push render targets outside a begun frame");
        }
        if (renderTargets.Length == 0) {
            throw new ArgumentException("at least one render target is required", nameof(renderTargets));
        }
        if (renderTargets.Length != clearValues.Length) {
            throw new ArgumentException("renderTargets and clearValues must have matching lengths");
        }

        CmdBuffer cmd = _mainCommandBuffers[_currentFrame];
        vkCmdEndRendering(cmd);

        _renderScopeStack.Push(_currentScope);

        RenderTarget[] targetsCopy = new RenderTarget[renderTargets.Length];
        for (int i = 0; i < renderTargets.Length; i++) {
            targetsCopy[i] = renderTargets[i];
        }

        VkClearValue[] clearCopy = new VkClearValue[clearValues.Length];
        for (int i = 0; i < clearValues.Length; i++) {
            clearCopy[i] = clearValues[i];
        }

        BeginRenderTargets(cmd, targetsCopy, clearCopy, VkAttachmentLoadOp.Clear, transitionToColorLayout: true);

        _currentScope = new RenderingScope {
            Type = RenderingScopeType.RenderTarget,
            RenderTargets = targetsCopy,
            ClearValues = clearCopy,
            Width = targetsCopy[0].Width,
            Height = targetsCopy[0].Height
        };

        return new RenderTargetScope(this);
    }

    public void PopRenderTargets() {
        if (!_isFrameStarted) {
            throw new InvalidOperationException("cannot pop render targets outside a begun frame");
        }
        if (_currentScope.Type != RenderingScopeType.RenderTarget) {
            throw new InvalidOperationException("no render targets are currently bound");
        }

        CmdBuffer cmd = _mainCommandBuffers[_currentFrame];
        vkCmdEndRendering(cmd);

        var targets = _currentScope.RenderTargets!;
        for (int i = 0; i < targets.Length; i++) {
            RenderTarget target = targets[i];
            if (target.CurrentLayout != VkImageLayout.ShaderReadOnlyOptimal) {
                cmd.TransitionImageLayout(target.Image, target.CurrentLayout, VkImageLayout.ShaderReadOnlyOptimal, aspects: VkImageAspectFlags.Color);
                target.CurrentLayout = VkImageLayout.ShaderReadOnlyOptimal;
            }
        }

        if (_renderScopeStack.Count == 0) {
            throw new InvalidOperationException("render target stack imbalance detected");
        }

        _currentScope = _renderScopeStack.Pop();

        if (_currentScope.Type == RenderingScopeType.Swapchain) {
            BeginSwapchainRendering(cmd, VkAttachmentLoadOp.Load, VkAttachmentLoadOp.Load, _currentScope.SwapchainClearValue);
        }
        else {
            BeginRenderTargets(cmd, _currentScope.RenderTargets!, _currentScope.ClearValues!, VkAttachmentLoadOp.Load, transitionToColorLayout: false);
        }
    }

    public void Submit(CmdBuffer cmd, Fence fence) => _graphicsQueue.Submit(cmd, fence);
    
    public void End() {
        if (!_isFrameStarted) {
            return;
        }

        CmdBuffer currentCmdBuffer = _mainCommandBuffers[_currentFrame]; 
        Semaphore currentImageAvailableSemaphore = _imageAvailableSemaphores[_currentFrame];
        Semaphore currentRenderFinishedSemaphore = _renderFinishedSemaphores[_imageIndex];
        Fence currentInFlightFence = _inFlightFences[_currentFrame];

        if (_currentScope.Type != RenderingScopeType.Swapchain || _renderScopeStack.Count != 0) {
            throw new InvalidOperationException("Render target scopes must be balanced before calling End().");
        }

        vkCmdEndRendering(currentCmdBuffer);

        Image swapchainImage = new(_logicalDevice, _mainSwapchain.Images[_imageIndex], _mainSwapchain.Width, _mainSwapchain.Height, _mainSwapchain.Format);
        currentCmdBuffer.TransitionImageLayout(swapchainImage, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.PresentSrcKHR, aspects: VkImageAspectFlags.Color);
        currentCmdBuffer.TransitionImageLayout(_depthImage.Image, VkImageLayout.DepthStencilAttachmentOptimal, VkImageLayout.DepthStencilReadOnlyOptimal, aspects: VkImageAspectFlags.Depth);
        
        currentCmdBuffer.End();

        _graphicsQueue.Submit(currentCmdBuffer, currentImageAvailableSemaphore, currentRenderFinishedSemaphore, currentInFlightFence);

        var result = _presentQueue.TryPresent(currentRenderFinishedSemaphore, _mainSwapchain, _imageIndex);

        if (result == VkResult.ErrorOutOfDateKHR || result == VkResult.SuboptimalKHR || _frameBufferResized) {
            _frameBufferResized = false;
            RecreateSwapchain();
        }
        else if (result != VkResult.Success) {
            throw new Exception($"failed to present swapchain image!: {result}");
        }

        _isFrameStarted = false;
        _currentFrame = (_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
    }
    
    private void CreateSwapchain() {
        var extent = _surface.ChooseSwapExtent(_physicalDevice);

        while (extent.width == 0 || extent.height == 0) {
            extent = _surface.ChooseSwapExtent(_physicalDevice);
        }
        
        _mainSwapchain = new(_logicalDevice, extent.width, extent.height, _surface);
        Console.WriteLine($"main backbuffer created: {extent.width}x{extent.height}");

        if (_imagesInFlight == null || _imagesInFlight.Length != _mainSwapchain.Images.Length) {
            _imagesInFlight = new VkFence[_mainSwapchain.Images.Length];
        }
        for (int i = 0; i < _imagesInFlight.Length; i++) {
            _imagesInFlight[i] = VkFence.Null;
        }
    }
    
    public void RecreateSwapchain() {
        var extent = _surface.ChooseSwapExtent(_physicalDevice);
        
        while (extent.width == 0 || extent.height == 0) {
            SDL.WaitEvent(out _);
            extent = _surface.ChooseSwapExtent(_physicalDevice);
        }
        
        vkDeviceWaitIdle(_logicalDevice);
        if (_renderFinishedSemaphores != null) {
            foreach(var semaphore in _renderFinishedSemaphores) {
                semaphore.Dispose();
            }
        }
        
        _depthImage.Dispose();

        CleanupSwapchain();
        CreateSwapchain();
        
        _renderFinishedSemaphores = new Semaphore[_mainSwapchain.Images.Length];
        for (int i = 0; i < _mainSwapchain.Images.Length; i++) {
             _renderFinishedSemaphores[i] = new(_logicalDevice);
        }
        
        CreateDepthResources();
    }

    private void CleanupSwapchain() {
        _mainSwapchain.Dispose();
    }
    
    private void CreateDepthResources() {
        var extent = _surface.ChooseSwapExtent(_physicalDevice);
        _depthImage = new DepthImage(_logicalDevice, extent.width, extent.height, _graphicsCmdPool, _graphicsQueue);
    }

    public void Dispose() {
        vkDeviceWaitIdle(_logicalDevice);
        
        _fences.Dispose();

        foreach(var fence in _inFlightFences) {
            fence.Dispose();
        }
        foreach(var semaphore in _imageAvailableSemaphores) {
            semaphore.Dispose();
        }
        if (_renderFinishedSemaphores != null) {
            foreach(var semaphore in _renderFinishedSemaphores) {
                semaphore.Dispose();
            }
        }
        foreach(var commandBuffer in _mainCommandBuffers) {
            commandBuffer.Dispose();
        }
        
        _graphicsCmdPool.Dispose();
        _mainSwapchain.Dispose();
        _depthImage.Dispose();
        _surface.Dispose();
    }

    private static readonly VkClearValue DefaultDepthClearValue = new() { depthStencil = new VkClearDepthStencilValue(1.0f, 0) };

    private void BeginSwapchainRendering(CmdBuffer cmd, VkAttachmentLoadOp colorLoadOp, VkAttachmentLoadOp depthLoadOp, VkClearValue clearColor) {
        VkRenderingAttachmentInfo colorAttachment = new()
        {
            sType = VkStructureType.RenderingAttachmentInfo,
            imageView = _mainSwapchain.ImageViews[_imageIndex],
            imageLayout = VkImageLayout.ColorAttachmentOptimal,
            loadOp = colorLoadOp,
            storeOp = VkAttachmentStoreOp.Store,
            clearValue = clearColor
        };

        VkRenderingAttachmentInfo depthAttachment = new()
        {
            sType = VkStructureType.RenderingAttachmentInfo,
            imageView = _depthImage.ImageView.Value,
            imageLayout = VkImageLayout.DepthStencilAttachmentOptimal,
            loadOp = depthLoadOp,
            storeOp = VkAttachmentStoreOp.DontCare,
            clearValue = DefaultDepthClearValue
        };

        VkRenderingInfo renderingInfo = new()
        {
            sType = VkStructureType.RenderingInfo,
            renderArea = new VkRect2D(0, 0, _mainSwapchain.Width, _mainSwapchain.Height),
            layerCount = 1,
            colorAttachmentCount = 1,
            pColorAttachments = &colorAttachment,
            pDepthAttachment = &depthAttachment,
            pStencilAttachment = null
        };

        vkCmdBeginRendering(cmd, &renderingInfo);
        CurrentRenderWidth = _mainSwapchain.Width;
        CurrentRenderHeight = _mainSwapchain.Height;
    }

    private void BeginRenderTargets(CmdBuffer cmd, RenderTarget[] targets, VkClearValue[] clearValues, VkAttachmentLoadOp loadOp, bool transitionToColorLayout) {
        int targetCount = targets.Length;
        Span<VkRenderingAttachmentInfo> attachments = stackalloc VkRenderingAttachmentInfo[targetCount];

        for (int i = 0; i < targetCount; i++) {
            RenderTarget target = targets[i];
            if (transitionToColorLayout && target.CurrentLayout != VkImageLayout.ColorAttachmentOptimal) {
                cmd.TransitionImageLayout(target.Image, target.CurrentLayout, VkImageLayout.ColorAttachmentOptimal, aspects: VkImageAspectFlags.Color);
                target.CurrentLayout = VkImageLayout.ColorAttachmentOptimal;
            }

            attachments[i] = new VkRenderingAttachmentInfo
            {
                sType = VkStructureType.RenderingAttachmentInfo,
                imageView = target.ImageView.Value,
                imageLayout = VkImageLayout.ColorAttachmentOptimal,
                loadOp = loadOp,
                storeOp = VkAttachmentStoreOp.Store,
                clearValue = clearValues[i]
            };
        }

        VkRenderingInfo renderingInfo = new()
        {
            sType = VkStructureType.RenderingInfo,
            renderArea = new VkRect2D(0, 0, targets[0].Width, targets[0].Height),
            layerCount = 1,
            colorAttachmentCount = (uint)targetCount,
            pDepthAttachment = null,
            pStencilAttachment = null
        };

        fixed (VkRenderingAttachmentInfo* pAttachments = attachments)
        {
            renderingInfo.pColorAttachments = pAttachments;
            vkCmdBeginRendering(cmd, &renderingInfo);
        }

        CurrentRenderWidth = targets[0].Width;
        CurrentRenderHeight = targets[0].Height;
    }

    private enum RenderingScopeType {
        Swapchain,
        RenderTarget
    }

    private struct RenderingScope {
        public RenderingScopeType Type;
        public RenderTarget[]? RenderTargets;
        public VkClearValue[]? ClearValues;
        public uint Width;
        public uint Height;
        public VkClearValue SwapchainClearValue;
    }

    public readonly struct RenderTargetScope : IDisposable {
        private readonly GraphicsDevice _device;

        internal RenderTargetScope(GraphicsDevice device) {
            _device = device;
        }

        public void Dispose() => _device.PopRenderTargets();
    }
}