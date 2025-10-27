using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie.Graphics;

public struct PhysicalDevice {
    public readonly VkPhysicalDevice Value;

    public PhysicalDevice(VkPhysicalDevice value) {
        Value = value;
    }
    
    public readonly VkPhysicalDeviceProperties GetProperties() {
        vkGetPhysicalDeviceProperties(Value, out VkPhysicalDeviceProperties properties);
        return properties;
    }
    
    public readonly ReadOnlySpan<VkExtensionProperties> GetExtensions() {
        return vkEnumerateDeviceExtensionProperties(Value);
    }
    
    public readonly VkPhysicalDeviceFeatures GetFeatures() {
        vkGetPhysicalDeviceFeatures(Value, out VkPhysicalDeviceFeatures features);
        return features;
    }
    
    public unsafe readonly VkSurfaceCapabilitiesKHR GetSurfaceCapabilities(VkSurfaceKHR surface) {
        VkSurfaceCapabilitiesKHR capabilities = default;
        VkResult result = vkGetPhysicalDeviceSurfaceCapabilitiesKHR(Value, surface, &capabilities);
        if (result != VkResult.Success)
        {
            throw new Exception($"Failed to get physical device surface capabilities: {result}");
        }

        return capabilities;
    }
    
    public readonly ReadOnlySpan<VkSurfaceFormatKHR> GetSurfaceFormats(VkSurfaceKHR surface)
    {
        return vkGetPhysicalDeviceSurfaceFormatsKHR(Value, surface);
    }

    public readonly ReadOnlySpan<VkPresentModeKHR> GetSurfacePresentModes(VkSurfaceKHR surface)
    {
        return vkGetPhysicalDeviceSurfacePresentModesKHR(Value, surface);
    }

    public readonly VkPhysicalDeviceLimits GetLimits()
    {
        vkGetPhysicalDeviceProperties(Value, out VkPhysicalDeviceProperties properties);
        return properties.limits;
    }

    public readonly ReadOnlySpan<VkQueueFamilyProperties> GetAllQueueFamilies()
    {
        return vkGetPhysicalDeviceQueueFamilyProperties(Value);
    }
    
    public VkFormatProperties GetFormatProperties(VkFormat format)
    {
        vkGetPhysicalDeviceFormatProperties(Value, format, out var properties);
        return properties;
    }

    public VkPhysicalDeviceMemoryProperties GetMemoryProperties() {
        vkGetPhysicalDeviceMemoryProperties(Value, out VkPhysicalDeviceMemoryProperties memoryProperties);
        return memoryProperties;
    }
    
    public readonly (uint? graphics, uint? present) GetQueueFamilies(VkSurfaceKHR surface)
    {
        ReadOnlySpan<VkQueueFamilyProperties> queueFamilies = GetAllQueueFamilies();
        uint graphicsFamily = VK_QUEUE_FAMILY_IGNORED;
        uint presentFamily = VK_QUEUE_FAMILY_IGNORED;
        for (uint i = 0; i < queueFamilies.Length; i++)
        {
            VkQueueFamilyProperties queueFamily = queueFamilies[(int)i];
            if ((queueFamily.queueFlags & VkQueueFlags.Graphics) != VkQueueFlags.None)
            {
                graphicsFamily = i;
                continue;
            }

            vkGetPhysicalDeviceSurfaceSupportKHR(Value, i, surface, out VkBool32 supportsPresenting);
            if (supportsPresenting)
            {
                presentFamily = i;
            }

            if (graphicsFamily != VK_QUEUE_FAMILY_IGNORED && presentFamily != VK_QUEUE_FAMILY_IGNORED)
            {
                break;
            }
        }

        return (graphicsFamily, presentFamily);
    }

    public readonly bool TryGetGraphicsQueueFamily(out uint graphicsFamily)
    {
        ReadOnlySpan<VkQueueFamilyProperties> queueFamilies = GetAllQueueFamilies();
        for (uint i = 0; i < queueFamilies.Length; i++)
        {
            VkQueueFamilyProperties queueFamily = queueFamilies[(int)i];
            if ((queueFamily.queueFlags & VkQueueFlags.Graphics) != VkQueueFlags.None)
            {
                graphicsFamily = i;
                return true;
            }
        }

        graphicsFamily = default;
        return false;
    }
}