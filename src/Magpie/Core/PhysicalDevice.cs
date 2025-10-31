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

    public QueueFamily FindQueueFamilies(PhysicalDevice device) {
        uint familyCount = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(Value, out familyCount);

        ReadOnlySpan<VkQueueFamilyProperties> families = stackalloc VkQueueFamilyProperties[(int)familyCount];
        fixed(VkQueueFamilyProperties* f = families)
            vkGetPhysicalDeviceQueueFamilyProperties(Value, &familyCount, f);
        
        uint graphicsFamily = VK_QUEUE_FAMILY_IGNORED;
        uint presentFamily = VK_QUEUE_FAMILY_IGNORED;
        
        QueueFamily family = default;
        
        for (uint i = 0; i < families.Length; i++) {
            VkQueueFamilyProperties queueFamily = families[(int)i];
            if ((queueFamily.queueFlags & VkQueueFlags.Graphics) != VkQueueFlags.None) {
                graphicsFamily = i;
                continue;
            }   

            if (graphicsFamily != VK_QUEUE_FAMILY_IGNORED) {
                break;
            }

            family = new QueueFamily { GraphicsFamily = graphicsFamily, PresentFamily = presentFamily };
        }

        return family;
    }

    public bool IsDeviceSuitable(PhysicalDevice device) {
        var indices = FindQueueFamilies(device);

        return indices.GraphicsFamily.HasValue;
    }
    
    public struct QueueFamily {
        public uint? GraphicsFamily { get; set; }
        public uint? PresentFamily { get; set; }
    
        public bool IsComplete() {
            return GraphicsFamily.HasValue && PresentFamily.HasValue;
        }
    }
}