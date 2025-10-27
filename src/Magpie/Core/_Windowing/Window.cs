using SDL3;
using System;
using Vortice.Vulkan;

namespace Magpie.Core;

public sealed unsafe class Window : IDisposable {
    private readonly nint _handle;
    private bool _disposed;

    public string Title { get; private set; }
    public uint Id { get; }

    public VkExtent2D Extent {
        get {
            SDL.GetWindowSize(_handle, out int width, out int height);
            return new VkExtent2D((uint)width, (uint)height);
        }
    }

    public Window(string title, int width, int height, SDL.WindowFlags flags) {
        Title = title;
        var sdlFlags = flags | SDL.WindowFlags.Vulkan | SDL.WindowFlags.HighPixelDensity;

        _handle = SDL.CreateWindow(title, width, height, sdlFlags);
        if (_handle == nint.Zero) {
            throw new Exception($"SDL: failed to create window: {SDL.GetError()}");
        }

        _ = SDL.SetWindowPosition(_handle, (int)SDL.WindowposCenteredMask, (int)SDL.WindowposCenteredMask);
        Id = SDL.GetWindowID(_handle);
        
        Show();
    }

    public void Show() {
        _ = SDL.ShowWindow(_handle);
    }

    public void SetTitle(string title) {
        Title = title;
        SDL.SetWindowTitle(_handle, title);
    }

    public VkSurfaceKHR CreateSurface(VkInstance instance) {
        if (!SDL.VulkanCreateSurface(_handle, instance, 0, out var surfaceHandle)) {
            throw new Exception($"SDL: failed to create vulkan surface: {SDL.GetError()}");
        }

        return new VkSurfaceKHR((ulong)surfaceHandle);
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        SDL.DestroyWindow(_handle);
        _disposed = true;
    }
}