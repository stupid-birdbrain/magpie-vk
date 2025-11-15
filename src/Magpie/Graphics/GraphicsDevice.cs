using Magpie.Core;
using Magpie.Utilities;
using Standard;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie.Graphics;

public sealed unsafe class GraphicsDevice : IDisposable {
    public VulkanInstance Instance;
    private Swapchain _mainSwapchain;
    private Surface _surface;
    private readonly CommandPool _graphicsCmdPool;
    private CommandBuffer _mainCommandBuffer;

    private readonly Queue _presentQueue;
    private readonly Queue _graphicsQueue;

    private readonly PhysicalDevice _physicalDevice;
    private readonly LogicalDevice _logicalDevice;

    private readonly Semaphore _imageAvailableSemaphore;
    private readonly Semaphore _renderFinishedSemaphore;
    private Fence _inFlightFence;

    private uint _imageIndex;
    private bool _isFrameStarted;
    
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

        _mainCommandBuffer = _graphicsCmdPool.CreateCommandBuffer();

        _imageAvailableSemaphore = new(_logicalDevice);
        _renderFinishedSemaphore = new(_logicalDevice);
        _inFlightFence = new(_logicalDevice);
    }
    
    public uint GetMemoryTypeIndex(uint typeBits, VkMemoryPropertyFlags properties) {
        vkGetPhysicalDeviceMemoryProperties(_physicalDevice.Value, out VkPhysicalDeviceMemoryProperties deviceMemoryProperties);

        for (int i = 0; i < deviceMemoryProperties.memoryTypeCount; i++) {
            if ((typeBits & 1) == 1) {
                if ((deviceMemoryProperties.memoryTypes[i].propertyFlags & properties) == properties) {
                    return (uint)i;
                }
            }
            typeBits >>= 1;
        }

        throw new Exception("Could not find a suitable memory type!");
    }
    
    public void Clear(VkClearValue clearColor) {
        var currentExtent = _surface.ChooseSwapExtent(_physicalDevice);

        if (currentExtent.width == 0 || currentExtent.height == 0) {
            return;
        }

        if (currentExtent.width != _mainSwapchain.Width || currentExtent.height != _mainSwapchain.Height) {
            RecreateSwapchain();
            return;
        }

        if (_isFrameStarted) {
            throw new InvalidOperationException("cannot call Clear twice in a frame!");
        }

        _inFlightFence.Wait();

        var result = vkAcquireNextImageKHR(_logicalDevice, _mainSwapchain.Value, ulong.MaxValue, _imageAvailableSemaphore, VkFence.Null, out _imageIndex);

        if (result == VkResult.ErrorOutOfDateKHR) {
            RecreateSwapchain();
            return;
        }
        else if (result != VkResult.Success && result != VkResult.SuboptimalKHR) {
            throw new Exception($"failed to acquire swapchain image!: {result}");
        }

        _isFrameStarted = true;
        _inFlightFence.Reset();
        _mainCommandBuffer.Reset();
        _mainCommandBuffer.Begin();

        TransitionImageLayout(_mainCommandBuffer, _mainSwapchain.Images[_imageIndex], VkImageLayout.Undefined, VkImageLayout.ColorAttachmentOptimal);

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

        vkCmdBeginRendering(_mainCommandBuffer, &renderingInfo);
    }
    
    public void Clear(Color color) => Clear(color.ToVkClearValue());
    
    public CommandBuffer AllocateCommandBuffer() {
        return _graphicsCmdPool.CreateCommandBuffer();
    }
    
    public CommandBuffer GetCurrentCommandBuffer() {
        if (!_isFrameStarted) {
            throw new InvalidOperationException("Cannot get command buffer before Clear() is called.");
        }
        return _mainCommandBuffer;
    }

    public void Submit(CommandBuffer cmd, Fence fence) {
        _graphicsQueue.Submit(cmd, fence);
    }
    
    public void Present() {
        if (!_isFrameStarted) {
            return;
        }

        vkCmdEndRendering(_mainCommandBuffer);

        TransitionImageLayout(
            _mainCommandBuffer,
            _mainSwapchain.Images[_imageIndex],
            VkImageLayout.ColorAttachmentOptimal,
            VkImageLayout.PresentSrcKHR
        );

        _mainCommandBuffer.End();

        _graphicsQueue.Submit(
            _mainCommandBuffer,
            _imageAvailableSemaphore,
            _renderFinishedSemaphore,
            _inFlightFence
        );

        var result = _presentQueue.TryPresent(
            _renderFinishedSemaphore,
            _mainSwapchain,
            _imageIndex
        );

        if (result == VkResult.ErrorOutOfDateKHR || result == VkResult.SuboptimalKHR) {
            RecreateSwapchain();
        }
        else if (result != VkResult.Success) {
            throw new Exception($"failed to present swapchain image!: {result}");
        }

        _isFrameStarted = false;
    }
    
    private void CreateSwapchain() {
        var extent = _surface.ChooseSwapExtent(_physicalDevice);

        while (extent.width == 0 || extent.height == 0) {
            extent = _surface.ChooseSwapExtent(_physicalDevice);
        }
        
        _mainSwapchain = new(_logicalDevice, extent.width, extent.height, _surface);
        Console.WriteLine($"main backbuffer created: {extent.width}x{extent.height}");
    }
    
    public void RecreateSwapchain() {
        vkDeviceWaitIdle(_logicalDevice);
        CleanupSwapchain();
        CreateSwapchain();
    }

    private void CleanupSwapchain() {
        _mainSwapchain.Dispose();
    }

    private void TransitionImageLayout(
        CommandBuffer cmd,
        VkImage image,
        VkImageLayout oldLayout,
        VkImageLayout newLayout
    )
    {
        VkImageMemoryBarrier barrier = new()
        {
            sType = VkStructureType.ImageMemoryBarrier,
            oldLayout = oldLayout,
            newLayout = newLayout,
            srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            image = image,
            subresourceRange = new VkImageSubresourceRange
            {
                aspectMask = VkImageAspectFlags.Color,
                baseMipLevel = 0,
                levelCount = 1,
                baseArrayLayer = 0,
                layerCount = 1
            }
        };

        VkPipelineStageFlags sourceStage;
        VkPipelineStageFlags destinationStage;

        if (
            oldLayout == VkImageLayout.Undefined
            && newLayout == VkImageLayout.ColorAttachmentOptimal
        )
        {
            barrier.srcAccessMask = 0;
            barrier.dstAccessMask = VkAccessFlags.ColorAttachmentWrite;
            sourceStage = VkPipelineStageFlags.TopOfPipe;
            destinationStage = VkPipelineStageFlags.ColorAttachmentOutput;
        }
        else if (
            oldLayout == VkImageLayout.ColorAttachmentOptimal
            && newLayout == VkImageLayout.PresentSrcKHR
        )
        {
            barrier.srcAccessMask = VkAccessFlags.ColorAttachmentWrite;
            barrier.dstAccessMask = 0;
            sourceStage = VkPipelineStageFlags.ColorAttachmentOutput;
            destinationStage = VkPipelineStageFlags.BottomOfPipe;
        }
        else
        {
            throw new NotSupportedException("Unsupported layout transition.");
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

    public void Dispose()
    {
        vkDeviceWaitIdle(_logicalDevice);
        
        _inFlightFence.Dispose();
        _renderFinishedSemaphore.Dispose();
        _imageAvailableSemaphore.Dispose();
        _graphicsCmdPool.Dispose();
        
        _mainSwapchain.Dispose();
        _surface.Dispose();
    }
}