using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie.Graphics;
    
/// <summary>
///     Represents a GPU. Contains info like vendor id, memory properties, avaliable extensions, etc.
/// </summary>
public unsafe struct PhysicalDevice(VkPhysicalDevice value) {
    internal readonly VkPhysicalDevice Value = value;
    public readonly nint Address => Value.Handle;
    
    public readonly VkPhysicalDeviceProperties GetProperties() {
        vkGetPhysicalDeviceProperties(Value, out VkPhysicalDeviceProperties properties);
        return properties;
    }
    
    public readonly VkPhysicalDeviceFeatures GetFeatures() {
        vkGetPhysicalDeviceFeatures(Value, out VkPhysicalDeviceFeatures features);
        return features;
    }
    
    public VkPhysicalDeviceMemoryProperties GetMemoryProperties() {
        vkGetPhysicalDeviceMemoryProperties(Value, out VkPhysicalDeviceMemoryProperties memoryProperties);
        return memoryProperties;
    }

    public readonly (uint graphics, uint present) FindQueueFamilies() {
        if (Value.IsNull) {
            throw new InvalidOperationException("invalid physical device handle.");
        }
        
        var queueFamilies = vkGetPhysicalDeviceQueueFamilyProperties(Value);
        uint graphicsFamily = VK_QUEUE_FAMILY_IGNORED;
        uint presentFamily = VK_QUEUE_FAMILY_IGNORED;
        for (uint i = 0; i < queueFamilies.Length; i++) {
            VkQueueFamilyProperties queueFamily = queueFamilies[(int)i];
            if ((queueFamily.queueFlags & VkQueueFlags.Graphics) != VkQueueFlags.None) {
                graphicsFamily = i;
                continue;
            }

            // vkGetPhysicalDeviceSurfaceSupportKHR(value, i, surface.value, out VkBool32 supportsPresenting);
            // if (supportsPresenting)
            // {
            //     presentFamily = i;
            // }

            if (graphicsFamily != VK_QUEUE_FAMILY_IGNORED)
            {
                break;
            }
        }

        return (graphicsFamily, presentFamily);
    }

    public readonly bool TryGetGraphicsQueueFamily(out uint graphicsFamily) {
        var queueFamilies = vkGetPhysicalDeviceQueueFamilyProperties(Value);
        for (uint i = 0; i < queueFamilies.Length; i++) {
            VkQueueFamilyProperties queueFamily = queueFamilies[(int)i];
            if ((queueFamily.queueFlags & VkQueueFlags.Graphics) != VkQueueFlags.None) {
                graphicsFamily = i;
                return true;
            }
        }

        graphicsFamily = default;
        return false;
    }
    
    public struct QueueFamily {
        public uint? GraphicsFamily { get; set; }
        public uint? PresentFamily { get; set; }
    
        public bool IsComplete() {
            return GraphicsFamily.HasValue && PresentFamily.HasValue;
        }
    }
}