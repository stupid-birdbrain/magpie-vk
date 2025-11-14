using System.Diagnostics;
using Magpie.Core;
using Magpie.Graphics;
using Magpie.Utilities;
using SDL3;
using ShaderCompilation;
using StainedGlass;
using Standard;
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
    
    private SdlWindow _windowHandle;
    private ShaderCompiler? _compiler;
    
    public void Initialize(string[] args) { 
        _compiler = new ShaderCompiler();
        
        _vkContext = new("vulkan");
        _sdlContext = new(SDL.InitFlags.Video | SDL.InitFlags.Events);
        _vkInstance = new(_vkContext, "magpieTests", "magpieco");
        
        _windowHandle = new("magpi", 400, 400, SDL.WindowFlags.Vulkan | SDL.WindowFlags.Resizable);
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
        Console.WriteLine("selected physical device info:" + bestDevice.ToString());
        
        var shaderBytes = _compiler.CompileShader("resources/shader.frag", ShaderKind.Fragment); 
        var reflectedData = _compiler.ReflectShader(shaderBytes.ToArray(), Backend.GLSL);
        
        Graphics = new (_vkInstance, _vkSurface, bestDevice, _vkDevice);
        
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
            
            Draw();
            
            Time.Stop();
        }
        
        Dispose();
    }

    void Draw() {
        Debug.Assert(Graphics != null);
        
        Graphics.Clear(Colors.MediumSlateBlue);

        Graphics.Present();
    }

    private void Dispose() {
        Vulkan.vkDeviceWaitIdle(_vkDevice);
        
        Graphics!.Dispose();
        
        _vkInstance.Dispose();
        _vkContext?.Dispose();
        _sdlContext?.Dispose();
        _windowHandle.Dispose();
    }
}