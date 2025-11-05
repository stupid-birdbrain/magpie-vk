using Magpie.Core;
using Magpie.Graphics;
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

    private SdlWindow _windowHandle;
    
    private ShaderCompiler? _compiler;
    
    private VkSurfaceKHR _vkSurface;
    
    public void Initialize(string[] args) { 
        if(SDL.Init(SDL.InitFlags.Video | SDL.InitFlags.Gamepad) == false) {
            var error = SDL.GetError();
            throw new Exception($"Failed to start SDL: {error}");
        }
        
        _compiler = new ShaderCompiler();
        
        _vkContext = new VkCtx("vulkan");
        _sdlContext = new SdlCtx(SDL.InitFlags.Video | SDL.InitFlags.Events);
        
        _vkInstance = new(_vkContext, "magpieTests", "magpieco");

        _windowHandle = new SdlWindow("magpi", 400, 400, SDL.WindowFlags.Vulkan |  SDL.WindowFlags.Transparent);

        _vkSurface = new((ulong)_windowHandle.CreateVulkanSurface(_vkInstance));
        
        if (!_vkInstance.TryGetBestPhysicalDevice(["VK_KHR_swapchain"], out var device)) {
            throw new InvalidOperationException("No valid physical device found");
        }   
        
        Console.WriteLine("selected physical device info:" + device.ToString());
        
        var shaderBytes = _compiler.CompileShader("resources/shader.frag", ShaderKind.Fragment); 
        var reflectedData = _compiler.ReflectShader(shaderBytes.ToArray(), Backend.GLSL);
        
        //Console.WriteLine(reflectedData);
        //Console.WriteLine(reflectedData.ReflectedCode);
        
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
        Vulkan.vkDestroySurfaceKHR(_vkInstance, _vkSurface, null);
        
        _vkInstance.Dispose();
        _vkContext?.Dispose();
        _sdlContext?.Dispose();
        _windowHandle.Dispose();
    }
}