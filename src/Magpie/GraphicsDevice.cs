using Magpie.Core;
using Magpie.Utilities;
using SDL3;
using Standard;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using Image = Magpie.Core.Image;
using Semaphore = Magpie.Core.Semaphore;

namespace Magpie;

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
    private bool _swapchainRenderingActive;

    public bool IsFrameStarted => _isFrameStarted;
    public Swapchain MainSwapchain => _mainSwapchain;
    
    public CmdPool GraphicsCommandPool => _graphicsCmdPool;
    public Queue GraphicsQueue => _graphicsQueue;
    public int CurrentFrameIndex => _currentFrame;
    public LogicalDevice LogicalDevice => _logicalDevice;
    public DepthImage DepthImage => _depthImage;
    

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
    ///     Attempts to begin a new rendering frame. Call <see cref="BeginSwapchainRendering"/> before drawing to the backbuffer.
    /// </summary>
    public bool BeginFrame() {
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
        if (imageFence.Handle != VkFence.Null.Handle) {
            vkWaitForFences(_logicalDevice, 1, &imageFence, true, ulong.MaxValue).CheckResult();
        }

        _imagesInFlight[(int)_imageIndex] = currentInFlightFence;

        _isFrameStarted = true;
        _swapchainRenderingActive = false;
        currentInFlightFence.Reset();

        currentCmdBuffer.Reset();
        currentCmdBuffer.Begin(flags: VkCommandBufferUsageFlags.OneTimeSubmit);

        return true;
    }

    public void BeginSwapchainRendering(VkClearValue clearColor, VkAttachmentLoadOp colorLoadOp = VkAttachmentLoadOp.Clear, bool clearDepth = true) {
        if (!_isFrameStarted) {
            throw new InvalidOperationException("cannot begin swapchain rendering before a frame has started.");
        }
        if (_swapchainRenderingActive) {
            throw new InvalidOperationException("swapchain rendering is already active.");
        }

        CmdBuffer currentCmdBuffer = _mainCommandBuffers[_currentFrame];
        Image swapchainImage = new(_logicalDevice, _mainSwapchain.Images[_imageIndex], _mainSwapchain.Width, _mainSwapchain.Height, _mainSwapchain.Format);
        currentCmdBuffer.TransitionImageLayout(swapchainImage, VkImageLayout.Undefined, VkImageLayout.ColorAttachmentOptimal, aspects: VkImageAspectFlags.Color);
        currentCmdBuffer.TransitionImageLayout(_depthImage.Image, VkImageLayout.Undefined, VkImageLayout.DepthStencilAttachmentOptimal, aspects: VkImageAspectFlags.Depth);

        VkRenderingAttachmentInfo colorAttachment = new() {
            sType = VkStructureType.RenderingAttachmentInfo,
            imageView = _mainSwapchain.ImageViews[_imageIndex],
            imageLayout = VkImageLayout.ColorAttachmentOptimal,
            loadOp = colorLoadOp,
            storeOp = VkAttachmentStoreOp.Store,
            clearValue = clearColor
        };

        VkClearValue depthClearValue = new() { depthStencil = new VkClearDepthStencilValue(1.0f, 0) };
        VkAttachmentLoadOp depthLoadOp = clearDepth ? VkAttachmentLoadOp.Clear : VkAttachmentLoadOp.Load;

        VkRenderingAttachmentInfo depthAttachment = new() {
            sType = VkStructureType.RenderingAttachmentInfo,
            imageView = _depthImage.ImageView.Value,
            imageLayout = VkImageLayout.DepthStencilAttachmentOptimal,
            loadOp = depthLoadOp,
            storeOp = VkAttachmentStoreOp.DontCare,
            clearValue = depthClearValue
        };

        VkRect2D renderArea = new VkRect2D(new VkOffset2D(0, 0), new VkExtent2D(_mainSwapchain.Width, _mainSwapchain.Height));
        VkRenderingInfo renderingInfo = new() {
            sType = VkStructureType.RenderingInfo,
            renderArea = renderArea,
            layerCount = 1,
            colorAttachmentCount = 1,
            pColorAttachments = &colorAttachment,
            pDepthAttachment = &depthAttachment,
            pStencilAttachment = null
        };

        vkCmdBeginRendering(currentCmdBuffer, &renderingInfo);
        _swapchainRenderingActive = true;
    }

    /// <summary>
    ///     Attempts to begin a new rendering frame and immediately start rendering to the swapchain.
    /// </summary>
    public bool Begin(VkClearValue clearColor) {
        if (!BeginFrame()) {
            return false;
        }

        BeginSwapchainRendering(clearColor);
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

    public void Submit(CmdBuffer cmd, Fence fence) => _graphicsQueue.Submit(cmd, fence);
    
    public void End() {
        if (!_isFrameStarted) {
            return;
        }

        CmdBuffer currentCmdBuffer = _mainCommandBuffers[_currentFrame]; 
        Semaphore currentImageAvailableSemaphore = _imageAvailableSemaphores[_currentFrame];
        Semaphore currentRenderFinishedSemaphore = _renderFinishedSemaphores[_imageIndex];
        Fence currentInFlightFence = _inFlightFences[_currentFrame];

        if (!_swapchainRenderingActive) {
            throw new InvalidOperationException("swapchain rendering was not started before End().");
        }

        vkCmdEndRendering(currentCmdBuffer);

        Image swapchainImage = new(_logicalDevice, _mainSwapchain.Images[_imageIndex], _mainSwapchain.Width, _mainSwapchain.Height, _mainSwapchain.Format);
        currentCmdBuffer.TransitionImageLayout(swapchainImage, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.PresentSrcKHR, aspects: VkImageAspectFlags.Color);
        currentCmdBuffer.TransitionImageLayout(_depthImage.Image, VkImageLayout.DepthStencilAttachmentOptimal, VkImageLayout.DepthStencilReadOnlyOptimal, aspects: VkImageAspectFlags.Depth);
        _swapchainRenderingActive = false;
        
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
}