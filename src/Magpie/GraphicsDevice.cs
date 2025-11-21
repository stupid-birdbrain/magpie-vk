using Magpie.Core;
using Magpie.Utilities;
using SDL3;
using Standard;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie.Graphics;

public sealed unsafe class GraphicsDevice : IDisposable {
    private const int max_frames_in_flight = 2;

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
    
    private bool _frameBufferResized = false;

    private readonly FencePool _fences;

    private uint _imageIndex;
    private bool _isFrameStarted;
    private int _currentFrame;

    public bool IsFrameStarted => _isFrameStarted;
    public Swapchain MainSwapchain => _mainSwapchain;
    
    public CmdPool GraphicsCommandPool => _graphicsCmdPool;
    public Queue GraphicsQueue => _graphicsQueue;
    
    public int CurrentFrameIndex => _currentFrame;

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

        _mainCommandBuffers = new CmdBuffer[max_frames_in_flight];
        _imageAvailableSemaphores = new Semaphore[max_frames_in_flight];
        _inFlightFences = new Fence[max_frames_in_flight];

        for (int i = 0; i < max_frames_in_flight; i++) {
            _mainCommandBuffers[i] = _graphicsCmdPool.CreateCommandBuffer();
            _imageAvailableSemaphores[i] = new(_logicalDevice);
            _inFlightFences[i] = new(_logicalDevice);
        }
        
        _renderFinishedSemaphores = new Semaphore[_mainSwapchain.Images.Length];
        for (int i = 0; i < _mainSwapchain.Images.Length; i++) {
             _renderFinishedSemaphores[i] = new(_logicalDevice);
        }

        _fences = new FencePool(_logicalDevice);
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

        TransitionImageLayout(currentCmdBuffer, _mainSwapchain.Images[_imageIndex], VkImageLayout.Undefined, VkImageLayout.ColorAttachmentOptimal);

        VkRenderingAttachmentInfo colorAttachment = new()
        {
            sType = VkStructureType.RenderingAttachmentInfo,
            imageView = _mainSwapchain.ImageViews[_imageIndex],
            imageLayout = VkImageLayout.ColorAttachmentOptimal,
            loadOp = VkAttachmentLoadOp.Clear,
            storeOp = VkAttachmentStoreOp.Store,
            clearValue = clearColor
        };

        VkRenderingInfo renderingInfo = new() {
            sType = VkStructureType.RenderingInfo,
            renderArea = new VkRect2D(0, 0, _mainSwapchain.Width, _mainSwapchain.Height),
            layerCount = 1,
            colorAttachmentCount = 1,
            pColorAttachments = &colorAttachment
        };

        vkCmdBeginRendering(currentCmdBuffer, &renderingInfo);
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

        vkCmdEndRendering(currentCmdBuffer);

        TransitionImageLayout(
            currentCmdBuffer,
            _mainSwapchain.Images[_imageIndex],
            VkImageLayout.ColorAttachmentOptimal,
            VkImageLayout.PresentSrcKHR
        );

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
        _currentFrame = (_currentFrame + 1) % max_frames_in_flight;
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

        CleanupSwapchain();
        CreateSwapchain();
        
        _renderFinishedSemaphores = new Semaphore[_mainSwapchain.Images.Length];
        for (int i = 0; i < _mainSwapchain.Images.Length; i++) {
             _renderFinishedSemaphores[i] = new(_logicalDevice);
        }
    }

    private void CleanupSwapchain() {
        _mainSwapchain.Dispose();
    }

    private void TransitionImageLayout(CmdBuffer cmd, VkImage image, VkImageLayout oldLayout, VkImageLayout newLayout) {
        VkImageMemoryBarrier barrier = new() {
            sType = VkStructureType.ImageMemoryBarrier,
            oldLayout = oldLayout,
            newLayout = newLayout,
            srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            image = image,
            subresourceRange = new VkImageSubresourceRange {
                aspectMask = VkImageAspectFlags.Color,
                baseMipLevel = 0,
                levelCount = 1,
                baseArrayLayer = 0,
                layerCount = 1
            }
        };

        VkPipelineStageFlags sourceStage;
        VkPipelineStageFlags destinationStage;

        if (oldLayout == VkImageLayout.Undefined && newLayout == VkImageLayout.ColorAttachmentOptimal) {
            barrier.srcAccessMask = 0;
            barrier.dstAccessMask = VkAccessFlags.ColorAttachmentWrite;
            sourceStage = VkPipelineStageFlags.TopOfPipe;
            destinationStage = VkPipelineStageFlags.ColorAttachmentOutput;
        }
        else if (oldLayout == VkImageLayout.ColorAttachmentOptimal && newLayout == VkImageLayout.PresentSrcKHR) {
            barrier.srcAccessMask = VkAccessFlags.ColorAttachmentWrite;
            barrier.dstAccessMask = 0;
            sourceStage = VkPipelineStageFlags.ColorAttachmentOutput;
            destinationStage = VkPipelineStageFlags.BottomOfPipe;
        }
        else {
            throw new NotSupportedException("unsupported layout transition!");
        }

        vkCmdPipelineBarrier(cmd, sourceStage, destinationStage, 0, 0, null, 0, null, 1, &barrier);
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
        _surface.Dispose();
    }
}