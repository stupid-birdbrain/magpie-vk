using Vortice.Vulkan;

namespace Magpie.Core;

public unsafe struct Surface : IDisposable {
    internal VkSurfaceKHR Value;
    public readonly ulong Address => Value.Handle;

    internal readonly VulkanInstance Instance;

    public Surface(VulkanInstance instance, VkSurfaceKHR origSurface) {
        Instance = instance;
        Value = origSurface;
    }

    public void Dispose() {
        Vulkan.vkDestroySurfaceKHR(Instance, Value, null);
    }
}