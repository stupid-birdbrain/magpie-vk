using Magpie.Core;
using Magpie.Graphics;
using SDL3;
using ShaderCompilation;
using Vortice.SpirvCross;

namespace Samples;

internal sealed class VkSample {
    private Window? _window;
    public bool Quit;

    public GraphicsDevice Graphics;
    private VkCtx _vkContext;
    private VulkanInstance _vkInstance;
    
    private ShaderCompiler? _compiler;
    
    public void Initialize(string[] args) { 
        if(SDL.Init(SDL.InitFlags.Video | SDL.InitFlags.Gamepad) == false) {
            var error = SDL.GetError();
            throw new Exception($"Failed to start SDL: {error}");
        }
        
        _window = new Window("magpievktests", 1200, 800, SDL.WindowFlags.Resizable);
        _compiler = new ShaderCompiler();
        _vkContext = new VkCtx("vulkan");
        
        _vkInstance = new(_vkContext, "magpieTests", "magpieco");

        var shaderBytes = _compiler.CompileShader("resources/shader.frag", ShaderKind.Fragment); 
        var reflectedData = _compiler.ReflectShader(shaderBytes.ToArray(), Backend.GLSL);
        
        Console.WriteLine(reflectedData);
        Console.WriteLine(reflectedData.ReflectedCode);
        
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
    }
}