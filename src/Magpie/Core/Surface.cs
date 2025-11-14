using Magpie.Graphics;
using Vortice.Vulkan;

namespace Magpie.Core;

public unsafe struct Surface : IDisposable {
    internal VkSurfaceKHR Value;
    public readonly nint Address => (IntPtr)Value.Handle;

    internal readonly VulkanInstance Instance;

    public Surface(VulkanInstance instance, nint origSurface) {
        Instance = instance;
        Value = new((ulong)origSurface);
    }
    
    public VkExtent2D ChooseSwapExtent(PhysicalDevice device) {
        uint width;
        uint height;
        
        var swapchainInfo = GetSwapchainDescription(device);
        VkSurfaceCapabilitiesKHR capabilities = swapchainInfo.Capabilities;
        if (capabilities.currentExtent.width == capabilities.minImageExtent.height) {
            width = default;
            height = default;
        }

        width = capabilities.currentExtent.width;
        height = capabilities.currentExtent.height;
        return new (width, height);
    }
    
    public (uint minWidth, uint maxWidth, uint minHeight, uint maxHeight) GetSizeRange(PhysicalDevice device) {
        var swapchainInfo = GetSwapchainDescription(device);
        VkSurfaceCapabilitiesKHR capabilities = swapchainInfo.Capabilities;
        return (capabilities.minImageExtent.width, capabilities.maxImageExtent.width, capabilities.minImageExtent.height, capabilities.maxImageExtent.height);
    }

    public SwapchainCapabilitiesDescription GetSwapchainDescription(PhysicalDevice physicalDevice) {
        var capabilities = physicalDevice.GetSurfaceCapabilities(this);
        var formats = physicalDevice.GetSurfaceFormats(this);
        var presentModes = physicalDevice.GetSurfacePresentModes(this);
        
        return new(capabilities, formats, presentModes);
    }

    public void Dispose() {
        if(Value != VkSurfaceKHR.Null) {
            Vulkan.vkDestroySurfaceKHR(Instance, Value, null);
            Value = VkSurfaceKHR.Null;
        }
    }
    
    public static implicit operator VkSurfaceKHR(Surface surf) => surf.Value;
}