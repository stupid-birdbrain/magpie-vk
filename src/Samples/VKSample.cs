using Magpie.Core;
using Magpie.Graphics;
using Magpie.Utilities;
using SDL3;
using ShaderCompilation;
using StainedGlass;
using Vortice.SpirvCross;
using Vortice.Vulkan;

namespace Samples;

internal sealed unsafe class VkSample {
    public bool Quit;

    public GraphicsDevice? Graphics;
    private VkCtx? _vkContext;
    private SdlCtx? _sdlContext;
    private VulkanInstance _vkInstance;
    private Surface _vkSurface;
    private LogicalDevice _vkDevice;
    private Swapchain _swapchain;
    
    private SdlWindow _windowHandle;
    private ShaderCompiler? _compiler;

    private Queue _presentQueue;
    
    public void Initialize(string[] args) { 
        _compiler = new ShaderCompiler();
        
        _vkContext = new("vulkan");
        _sdlContext = new(SDL.InitFlags.Video | SDL.InitFlags.Events);
        _vkInstance = new(_vkContext, "magpieTests", "magpieco");
        
        _windowHandle = new("magpi", 400, 400, SDL.WindowFlags.Vulkan | SDL.WindowFlags.Transparent | SDL.WindowFlags.Resizable);
        _vkSurface = new(_vkInstance, _windowHandle.CreateVulkanSurface(_vkInstance));
        
        string[] requiredDeviceExtensions = [
            new VkUtf8String(Vulkan.VK_KHR_SWAPCHAIN_EXTENSION_NAME.GetPointer()).ToString()!,
            new VkUtf8String(Vulkan.VK_EXT_DESCRIPTOR_INDEXING_EXTENSION_NAME.GetPointer()).ToString()!,
            new VkUtf8String(Vulkan.VK_KHR_RAY_TRACING_PIPELINE_EXTENSION_NAME.GetPointer()).ToString()!,
            new VkUtf8String(Vulkan.VK_KHR_ACCELERATION_STRUCTURE_EXTENSION_NAME.GetPointer()).ToString()!,
            new VkUtf8String(Vulkan.VK_KHR_DEFERRED_HOST_OPERATIONS_EXTENSION_NAME.GetPointer()).ToString()!,
            "VK_KHR_spirv_1_4",
        ];
        
        PhysicalDevice bestDevice;
        if (!_vkInstance.TryGetBestPhysicalDevice(requiredDeviceExtensions, out bestDevice)) {
            throw new InvalidOperationException("no valid physical device found");
        }   
        
        uint graphicsQueueFamilyIndex;
        if (!bestDevice.TryGetGraphicsQueueFamily(out graphicsQueueFamilyIndex)) {
            throw new Exception("selected physical device does not have a graphics queue family");
        }

        _vkDevice = new(bestDevice, [graphicsQueueFamilyIndex], requiredDeviceExtensions);
        _presentQueue = _vkDevice.GetQueue(graphicsQueueFamilyIndex, 0);
        
        Graphics = new (_vkInstance, _vkSurface, bestDevice, _vkDevice);
        
        Console.WriteLine("selected physical device info:" + bestDevice.ToString());
        
        var shaderBytes = _compiler.CompileShader("resources/shader.frag", ShaderKind.Fragment); 
        var reflectedData = _compiler.ReflectShader(shaderBytes.ToArray(), Backend.GLSL);
        
        // var swapchainInfo = _vkSurface.GetSwapchainDescription(_vkDevice.PhysicalDevice);
        // var surfaceFormat = swapchainInfo.ChooseSwapSurfaceFormat();
        // var presentMode = swapchainInfo.ChooseSwapPresentMode();
        // var extent = _vkSurface.ChooseSwapExtent(bestDevice);
        //
        // uint imageCount = swapchainInfo.Capabilities.minImageCount + 1;
        // if (swapchainInfo.Capabilities.maxImageCount > 0 && imageCount > swapchainInfo.Capabilities.maxImageCount) {
        //     imageCount = swapchainInfo.Capabilities.maxImageCount;
        // }
        //
        //
        // uint swapchainImageCount;
        // Vulkan.vkGetSwapchainImagesKHR(_vkDevice, _swapchain, &swapchainImageCount, null);
        // VkImage* swapchainImages = stackalloc VkImage[(int)swapchainImageCount];
        // Vulkan.vkGetSwapchainImagesKHR(_vkDevice, _swapchain, &swapchainImageCount, swapchainImages);
        //
        // Console.WriteLine($"swapchain images retrieved, amt: {swapchainImageCount}");
        // for (int i = 0; i < swapchainImageCount; i++) {
        //     Console.WriteLine($"img {i}: {swapchainImages[i]}");
        // }
        //
        // // Console.WriteLine(surfaceFormat.format);
        // // Console.WriteLine(surfaceFormat.colorSpace);
        // // Console.WriteLine(presentMode);
        // // Console.WriteLine(extent);
        //
        // _imageViews = new VkImageView[swapchainImageCount];
        //
        // for(int i = 0; i < swapchainImageCount; i++) {
        //     VkImageViewCreateInfo createInfo = new()
        //     {
        //         sType = VkStructureType.ImageViewCreateInfo,
        //         image = swapchainImages[i],
        //         viewType = VkImageViewType.Image2D,
        //         format = surfaceFormat.format,
        //         
        //         subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, baseMipLevel: 0, levelCount: 1, baseArrayLayer: 0, layerCount: 1) 
        //     };
        //     
        //     fixed (VkImageView* pImageView = &_imageViews[i]) {
        //         var result = Vulkan.vkCreateImageView(_vkDevice, &createInfo, null, pImageView);
        //         if (result != VkResult.Success) {
        //             throw new Exception($"failed to create image view {i}! result: {result}");
        //         }
        //     }
        // }
        //
        while (!Quit) {
            Time.Start();
            while (SDL.PollEvent(out var @event)) {
                switch (@event.Type) {
                    case (uint) SDL.EventType.Quit:
                        Quit = true;
                        break;
                    case (uint) SDL.EventType.KeyDown:
                        if (@event.Key.Key == SDL.Keycode.Escape)
                            Quit = true;
                        break;
                }
            }
        
            Time.Update();
            
            Time.Stop();
        }
        
        Dispose();
    }

    private void Dispose() {
        Vulkan.vkDeviceWaitIdle(_vkDevice);
        
        _swapchain.Dispose();
        _vkDevice.Dispose();
        _vkSurface.Dispose();
        
        _vkInstance.Dispose();
        _vkContext?.Dispose();
        _sdlContext?.Dispose();
        _windowHandle.Dispose();
    }
}