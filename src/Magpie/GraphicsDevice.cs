using Magpie.Core;
using Magpie.Utilities;
using Standard;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie.Graphics;

public sealed unsafe class GraphicsDevice : IDisposable {
    private const int max_frames_in_flight = 2;

    public VulkanInstance Instance;
    private Swapchain _mainSwapchain;
    private Surface _surface;
    private CommandPool _graphicsCmdPool;
    private readonly CommandBuffer[] _mainCommandBuffers; //one buffer per frame in flight

    private readonly Queue _presentQueue;
    private readonly Queue _graphicsQueue;

    private readonly PhysicalDevice _physicalDevice;
    private readonly LogicalDevice _logicalDevice;

    private readonly Semaphore[] _imageAvailableSemaphores;
    private readonly Semaphore[] _renderFinishedSemaphores;
    private readonly Fence[] _inFlightFences;
    private VkFence[] _imagesInFlight = null!;

    private readonly FencePool _fences;

    private uint _imageIndex;
    private bool _isFrameStarted;
    private int _currentFrame;

    public bool IsFrameStarted => _isFrameStarted;
    public Swapchain MainSwapchain => _mainSwapchain;

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

        _mainCommandBuffers = new CommandBuffer[max_frames_in_flight];
        for (int i = 0; i < max_frames_in_flight; i++) {
            _mainCommandBuffers[i] = _graphicsCmdPool.CreateCommandBuffer();
        }

        _imageAvailableSemaphores = new Semaphore[max_frames_in_flight];
        _renderFinishedSemaphores = new Semaphore[max_frames_in_flight];
        _inFlightFences = new Fence[max_frames_in_flight];

        for (int i = 0; i < max_frames_in_flight; i++) {
            _imageAvailableSemaphores[i] = new(_logicalDevice);
            _renderFinishedSemaphores[i] = new(_logicalDevice);
            _inFlightFences[i] = new(_logicalDevice);
        }

        _fences = new FencePool(_logicalDevice);
    }
    
    public FenceLease RequestFence(VkFenceCreateFlags flags) => _fences.Rent(flags);
    
    public uint GetMemoryTypeIndex(uint typeBits, VkMemoryPropertyFlags properties) {
        vkGetPhysicalDeviceMemoryProperties(_physicalDevice.Value, out VkPhysicalDeviceMemoryProperties deviceMemoryProperties);

        for (int i = 0; i < deviceMemoryProperties.memoryTypeCount; i++) {
            if (((typeBits >> i) & 1) == 1) {
                if ((deviceMemoryProperties.memoryTypes[i].propertyFlags & properties) == properties) {
                    return (uint)i;
                }
            }
        }

        throw new Exception("Could not find a suitable memory type!");
    }
    
    public void Clear(VkClearValue clearColor) {
        var currentExtent = _surface.ChooseSwapExtent(_physicalDevice);

        if (currentExtent.width == 0 || currentExtent.height == 0) {
            _isFrameStarted = false;
            return;
        }

        if (currentExtent.width != _mainSwapchain.Width || currentExtent.height != _mainSwapchain.Height) {
            RecreateSwapchain();
            _isFrameStarted = false;
            return;
        }

        if (_isFrameStarted) {
            throw new InvalidOperationException("cannot call Clear twice in a frame!");
        }

        Fence currentInFlightFence = _inFlightFences[_currentFrame];
        CommandBuffer currentCommandBuffer = _mainCommandBuffers[_currentFrame];

        currentInFlightFence.Wait(); 

        VkResult result = vkAcquireNextImageKHR(
            _logicalDevice,
            _mainSwapchain.Value,
            ulong.MaxValue,
            _imageAvailableSemaphores[_currentFrame],
            VkFence.Null,
            out _imageIndex
        );

        if (result == VkResult.ErrorOutOfDateKHR) {
            RecreateSwapchain();
            _isFrameStarted = false;
            return;
        }
        else if (result != VkResult.Success && result != VkResult.SuboptimalKHR) {
            throw new Exception($"failed to acquire swapchain image!: {result}");
        }

        VkFence fenceToWaitOn = _imagesInFlight[(int)_imageIndex];
        if (fenceToWaitOn.Handle != VkFence.Null.Handle)
        {
            vkWaitForFences(_logicalDevice, 1, &fenceToWaitOn, true, ulong.MaxValue).CheckResult();
        }
        _imagesInFlight[(int)_imageIndex] = currentInFlightFence;

        _isFrameStarted = true;
        currentInFlightFence.Reset();

        currentCommandBuffer.Reset();
        currentCommandBuffer.Begin();

        TransitionImageLayout(currentCommandBuffer, _mainSwapchain.Images[_imageIndex], VkImageLayout.Undefined, VkImageLayout.ColorAttachmentOptimal);

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

        vkCmdBeginRendering(currentCommandBuffer, &renderingInfo);
    }
    
    public void Clear(Color color) => Clear(color.ToVkClearValue());
    
    public CommandBuffer AllocateCommandBuffer(bool primary) {
        return _graphicsCmdPool.CreateCommandBuffer(primary);
    }
    
    public CommandBuffer RequestCurrentCommandBuffer() {
        if (!_isFrameStarted) {
            throw new InvalidOperationException("Ccnnot get command buffer before Clear() is called!");
        }
        
        return _mainCommandBuffers[_currentFrame];
    }

    public void Submit(CommandBuffer cmd, Fence fence) {
        _graphicsQueue.Submit(cmd, fence);
    }
    
    public void Present() {
        if (!_isFrameStarted) {
            return;
        }

        CommandBuffer currentCommandBuffer = _mainCommandBuffers[_currentFrame]; 

        vkCmdEndRendering(currentCommandBuffer);

        TransitionImageLayout(
            currentCommandBuffer,
            _mainSwapchain.Images[_imageIndex],
            VkImageLayout.ColorAttachmentOptimal,
            VkImageLayout.PresentSrcKHR
        );

        currentCommandBuffer.End();

        _graphicsQueue.Submit(
            currentCommandBuffer,
            _imageAvailableSemaphores[_currentFrame],
            _renderFinishedSemaphores[_currentFrame],
            _inFlightFences[_currentFrame]
        );

        var result = _presentQueue.TryPresent(_renderFinishedSemaphores[_currentFrame], _mainSwapchain, _imageIndex);

        if (result == VkResult.ErrorOutOfDateKHR || result == VkResult.SuboptimalKHR) {
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

        _imagesInFlight = new VkFence[_mainSwapchain.Images.Length];
    }
    
    public void RecreateSwapchain() {
        vkDeviceWaitIdle(_logicalDevice);

        CleanupSwapchain();
        CreateSwapchain();
    }

    private void CleanupSwapchain() {
        _mainSwapchain.Dispose();
    }

    private void TransitionImageLayout(CommandBuffer cmd, VkImage image, VkImageLayout oldLayout, VkImageLayout newLayout) {
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

        vkCmdPipelineBarrier(
            cmd,
            sourceStage,
            destinationStage,
            0,
            0,
            null,
            0,
            null,
            1,
            &barrier
        );
    }

    public void Dispose() {
        vkDeviceWaitIdle(_logicalDevice); 
        
        _fences.Dispose();

        foreach(var fence in _inFlightFences) {
            fence.Dispose();
        }

        foreach(var semaphore in _renderFinishedSemaphores) {
            semaphore.Dispose();
        }

        foreach(var semaphore in _imageAvailableSemaphores) {
            semaphore.Dispose();
        }

        foreach(var commandBuffer in _mainCommandBuffers) {
            commandBuffer.Dispose();
        }
        
        _graphicsCmdPool.Dispose();
        _mainSwapchain.Dispose();
        _surface.Dispose();
    }
}