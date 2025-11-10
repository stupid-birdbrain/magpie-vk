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
    private VkSwapchainKHR _swapchain;
    
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
        
        Console.WriteLine("selected physical device info:" + bestDevice.ToString());
        
        var shaderBytes = _compiler.CompileShader("resources/shader.frag", ShaderKind.Fragment); 
        var reflectedData = _compiler.ReflectShader(shaderBytes.ToArray(), Backend.GLSL);
        
        
        var asd = _vkSurface.GetSwapchainDescription(_vkDevice.PhysicalDevice);
        var surfaceFormat = asd.ChooseSwapSurfaceFormat();
        var presentMode = asd.ChooseSwapPresentMode();
        var extent = _vkSurface.ChooseSwapExtent(bestDevice);
        
        Console.WriteLine(surfaceFormat.format);
        Console.WriteLine(presentMode);
        Console.WriteLine(extent);
        
        VkSurfaceCapabilitiesKHR capabilities = bestDevice.GetSurfaceCapabilities(_vkSurface);
        uint imageCount = capabilities.minImageCount + 1;
        if (capabilities.maxImageCount > 0 && imageCount > capabilities.maxImageCount) {
            imageCount = capabilities.maxImageCount;
        }

        var swapchainInfo = _vkSurface.GetSwapchainDescription(_vkDevice.PhysicalDevice);

        VkSwapchainCreateInfoKHR swapchainCreateInfo = new() {
            sType = VkStructureType.SwapchainCreateInfoKHR,
            surface = _vkSurface,

            minImageCount = imageCount,
            imageFormat = surfaceFormat.format,
            imageColorSpace = surfaceFormat.colorSpace,
            imageExtent = extent,
            imageArrayLayers = 1,
            imageUsage = VkImageUsageFlags.ColorAttachment,

            imageSharingMode = VkSharingMode.Exclusive,
            queueFamilyIndexCount = 0,
            pQueueFamilyIndices = null,

            preTransform = capabilities.currentTransform,
            compositeAlpha = VkCompositeAlphaFlagsKHR.Opaque,
            presentMode = presentMode,
            clipped = true,
            oldSwapchain = VkSwapchainKHR.Null,
        };
        var queueFamilies = bestDevice.FindQueueFamilies(_vkSurface);
        uint graphicsQueueFamily = queueFamilies.GraphicsFamily!.Value;
        uint presentQueueFamily = queueFamilies.PresentFamily!.Value;

        if (graphicsQueueFamily != presentQueueFamily) {
            uint* queueFamilyIndices = stackalloc uint[] { graphicsQueueFamily, presentQueueFamily };
            swapchainCreateInfo.imageSharingMode = VkSharingMode.Concurrent;
            swapchainCreateInfo.queueFamilyIndexCount = 2;
            swapchainCreateInfo.pQueueFamilyIndices = queueFamilyIndices;
        }
        
        var swapchainResult = Vulkan.vkCreateSwapchainKHR(_vkDevice, &swapchainCreateInfo, null, out _swapchain);
        if (swapchainResult != VkResult.Success) {
            throw new Exception($"Failed to create swapchain! {swapchainResult}");
        }
        
        Console.WriteLine($"Swapchain created: {_swapchain}");

        uint swapchainImageCount;
        Vulkan.vkGetSwapchainImagesKHR(_vkDevice, _swapchain, &swapchainImageCount, null);
        VkImage* swapchainImages = stackalloc VkImage[(int)swapchainImageCount];
        Vulkan.vkGetSwapchainImagesKHR(_vkDevice, _swapchain, &swapchainImageCount, swapchainImages);

        Console.WriteLine($"Swapchain images retrieved. Count: {swapchainImageCount}");
        for (int i = 0; i < swapchainImageCount; i++) {
            Console.WriteLine($"  Image {i}: {swapchainImages[i]}");
        }
        
        while (Quit == false) {
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
        Vulkan.vkDestroySwapchainKHR(_vkDevice, _swapchain, null);
        _vkDevice.Dispose();
        _vkSurface.Dispose();
        
        _vkInstance.Dispose();
        _vkContext?.Dispose();
        _sdlContext?.Dispose();
        _windowHandle.Dispose();
    }
}