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
    private CommandPool _graphicsCmdPool;
    private CommandBuffer _mainCommandBuffer;

    private Queue _presentQueue;
    private Queue _graphicsQueue;
    
    private PhysicalDevice _physicalDevice;
    private LogicalDevice _logicalDevice;
    
    private readonly Semaphore _imageAvailableSemaphore;
    private readonly Semaphore _renderFinishedSemaphore;
    private Fence _inFlightFence;
    
    private VkRenderPass _renderPass;
    private VkFramebuffer[] _framebuffers;

    private uint _imageIndex;
    private bool _isFrameStarted;
    
    public GraphicsDevice(VulkanInstance instance, Surface surface, PhysicalDevice physicalDevice, LogicalDevice logicalDevice) {
        Instance = instance;
        _surface = surface;
        _physicalDevice = physicalDevice;
        _logicalDevice = logicalDevice;
        
        var queueFamilies = _physicalDevice.FindQueueFamilies(_surface);
        _graphicsQueue = _logicalDevice.GetQueue(queueFamilies.GraphicsFamily!.Value, 0);
        _presentQueue = _logicalDevice.GetQueue(queueFamilies.PresentFamily!.Value, 0);
        Console.WriteLine($"{queueFamilies.GraphicsFamily}, {queueFamilies.PresentFamily}");
        
        var extent = _surface.ChooseSwapExtent(_physicalDevice);
        _mainSwapchain = new(_logicalDevice, extent.width, extent.height, _surface);
        Console.WriteLine($"main backbuffer swapchain created!");
        
        CreateRenderPass();
        CreateFramebuffers();

        _graphicsCmdPool = new(_logicalDevice, _graphicsQueue);
        Console.WriteLine($"main cmd pool created!");

        _mainCommandBuffer = _graphicsCmdPool.CreateCommandBuffer();

        _imageAvailableSemaphore = new(_logicalDevice);
        _renderFinishedSemaphore = new(_logicalDevice);
        _inFlightFence = new(_logicalDevice);
    }
    
    private void CreateRenderPass() {
        VkAttachmentDescription colorAttachment = new() {
            format = _mainSwapchain.Format,
            samples = VkSampleCountFlags.Count1,
            loadOp = VkAttachmentLoadOp.Clear,
            storeOp = VkAttachmentStoreOp.Store,
            stencilLoadOp = VkAttachmentLoadOp.DontCare,
            stencilStoreOp = VkAttachmentStoreOp.DontCare,
            initialLayout = VkImageLayout.Undefined,
            finalLayout = VkImageLayout.PresentSrcKHR
        };

        VkAttachmentReference colorAttachmentRef = new() {
            attachment = 0,
            layout = VkImageLayout.ColorAttachmentOptimal
        };

        VkSubpassDescription subpass = new() {
            pipelineBindPoint = VkPipelineBindPoint.Graphics,
            colorAttachmentCount = 1,
            pColorAttachments = &colorAttachmentRef
        };

        VkSubpassDependency dependency = new() {
            srcSubpass = VK_SUBPASS_EXTERNAL,
            dstSubpass = 0,
            srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput,
            srcAccessMask = 0,
            dstStageMask = VkPipelineStageFlags.ColorAttachmentOutput,
            dstAccessMask = VkAccessFlags.ColorAttachmentWrite
        };

        VkRenderPassCreateInfo renderPassInfo = new() {
            sType = VkStructureType.RenderPassCreateInfo,
            attachmentCount = 1,
            pAttachments = &colorAttachment,
            subpassCount = 1,
            pSubpasses = &subpass,
            dependencyCount = 1,
            pDependencies = &dependency
        };

        vkCreateRenderPass(_logicalDevice, &renderPassInfo, null, out _renderPass);
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

        VkResult result = vkAcquireNextImageKHR(
            _logicalDevice,
            _mainSwapchain.Value,
            ulong.MaxValue,
            _imageAvailableSemaphore,
            VkFence.Null,
            out _imageIndex
        );

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

        VkRenderPassBeginInfo renderPassInfo = new()
        {
            sType = VkStructureType.RenderPassBeginInfo,
            renderPass = _renderPass,
            framebuffer = _framebuffers[_imageIndex],
            renderArea = new VkRect2D(0, 0, _mainSwapchain.Width, _mainSwapchain.Height),
            clearValueCount = 1,
            pClearValues = &clearColor
        };

        vkCmdBeginRenderPass(
            _mainCommandBuffer.Value,
            &renderPassInfo,
            VkSubpassContents.Inline
        );
    }
    
    public void Clear(Color color) => Clear(color.ToVkClearValue());
    
    public void Present() {
        if (!_isFrameStarted) {
            return;
        }

        vkCmdEndRenderPass(_mainCommandBuffer.Value);
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
    
    private void CreateFramebuffers() {
        var imageViews = _mainSwapchain.ImageViews;
        _framebuffers = new VkFramebuffer[imageViews.Length];

        for (int i = 0; i < imageViews.Length; i++) {
            var attachment = imageViews[i];
            VkFramebufferCreateInfo framebufferInfo = new() {
                sType = VkStructureType.FramebufferCreateInfo,
                renderPass = _renderPass,
                attachmentCount = 1,
                pAttachments = &attachment,
                width = _mainSwapchain.Width,
                height = _mainSwapchain.Height,
                layers = 1
            };

            vkCreateFramebuffer(_logicalDevice, &framebufferInfo, null, out _framebuffers[i]);
        }
    }
    
    private void CreateSwapchain() {
        var extent = _surface.ChooseSwapExtent(_physicalDevice);
        
        _mainSwapchain = new(_logicalDevice, extent.width, extent.height, _surface);
        Console.WriteLine($"main backbuffer swapchain created!");
    }
    
    public void RecreateSwapchain() {
        vkDeviceWaitIdle(_logicalDevice);

        CleanupSwapchain();
        CreateSwapchain();
        CreateFramebuffers();
    }

    private void CleanupSwapchain() {
        foreach (var framebuffer in _framebuffers) {
            vkDestroyFramebuffer(_logicalDevice, framebuffer, null);
        }

        _mainSwapchain.Dispose();
    }

    public void Dispose() {
        vkDeviceWaitIdle(_logicalDevice);
        
        foreach (var framebuffer in _framebuffers) {
            vkDestroyFramebuffer(_logicalDevice, framebuffer, null);
        }
        vkDestroyRenderPass(_logicalDevice, _renderPass, null);

        _inFlightFence.Dispose();
        _renderFinishedSemaphore.Dispose();
        _imageAvailableSemaphore.Dispose();
        _graphicsCmdPool.Dispose();
        
        _mainSwapchain.Dispose();
        _surface.Dispose();
        _logicalDevice.Dispose();
    }
}