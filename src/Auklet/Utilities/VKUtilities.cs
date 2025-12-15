using Vortice.Vulkan;

namespace Magpie.Utilities;

public ref struct SwapChainSupportDetails
{
    public VkSurfaceCapabilitiesKHR Capabilities;
    public ReadOnlySpan<VkSurfaceFormatKHR> Formats;
    public ReadOnlySpan<VkPresentModeKHR> PresentModes;
}

public static class VKUtilities {
    public static uint FindMemoryType(VkPhysicalDevice device, uint typeFilter, VkMemoryPropertyFlags properties) {
        Vulkan.vkGetPhysicalDeviceMemoryProperties(device, out var memProperties);

        for (int i = 0; i < memProperties.memoryTypeCount; i++) {
            if ((typeFilter & (1 << i)) != 0 && (memProperties.memoryTypes[i].propertyFlags & properties) == properties) {
                return (uint)i;
            }
        }

        throw new Exception("failed to find suitable memory type!");
    }
    
    public static SwapChainSupportDetails QuerySwapChainSupport(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface)
    {
        SwapChainSupportDetails details = new SwapChainSupportDetails();
        Vulkan.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice, surface, out details.Capabilities).CheckResult();

        details.Formats = Vulkan.vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, surface);
        details.PresentModes = Vulkan.vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, surface);
        return details;
    }
}