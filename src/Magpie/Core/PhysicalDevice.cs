using System.Text;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie.Graphics;
    
/// <summary>
///     Represents a GPU. Contains info like vendor id, memory properties, avaliable extensions, etc.
/// </summary>
public readonly unsafe struct PhysicalDevice(VkPhysicalDevice value) : IEquatable<PhysicalDevice> {
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
    
    public readonly ReadOnlySpan<VkExtensionProperties> GetExtensions() {
        return vkEnumerateDeviceExtensionProperties(Value);
    }

    public readonly QueueFamily FindQueueFamilies(VkSurfaceKHR surface) {
        if (Value.IsNull) {
            throw new InvalidOperationException("Invalid physical device handle.");
        }
        
        uint familyCount;
        vkGetPhysicalDeviceQueueFamilyProperties(Value, out familyCount);
        
        ReadOnlySpan<VkQueueFamilyProperties> families = stackalloc VkQueueFamilyProperties[(int)familyCount];
        fixed(VkQueueFamilyProperties* f = families)
            vkGetPhysicalDeviceQueueFamilyProperties(Value, &familyCount, f);

        uint? graphicsFamilyIndex = null;
        uint? presentFamilyIndex = null;

        for (uint i = 0; i < families.Length; i++) {
            VkQueueFamilyProperties queueFamily = families[(int)i];

            if ((queueFamily.queueFlags & VkQueueFlags.Graphics) != VkQueueFlags.None) {
                graphicsFamilyIndex = i;
            }

            vkGetPhysicalDeviceSurfaceSupportKHR(Value, i, surface, out var supportsPresenting);
            if (supportsPresenting) {
                presentFamilyIndex = i;
            }

            if (graphicsFamilyIndex.HasValue && presentFamilyIndex.HasValue) {
                break;
            }
        }

        return new QueueFamily() {GraphicsFamily = graphicsFamilyIndex, PresentFamily = presentFamilyIndex};
    }
    
    public bool TryGetGraphicsQueueFamily(out uint graphicsFamily) {
        var queueFamilies = vkGetPhysicalDeviceQueueFamilyProperties(Value);
        for (uint i = 0; i < queueFamilies.Length; i++) {
            var family = queueFamilies[(int)i];
            if ((family.queueFlags & VkQueueFlags.Graphics) != VkQueueFlags.None) {
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
    
    public ulong GetTotalGpuMemoryBytes() {
        var memoryProperties = GetMemoryProperties();
        ulong totalDeviceLocalMemory = 0;
        for (int i = 0; i < memoryProperties.memoryHeapCount; i++) {
            var heap = memoryProperties.memoryHeaps[i];
            if ((heap.flags & VkMemoryHeapFlags.DeviceLocal) != VkMemoryHeapFlags.None) {
                totalDeviceLocalMemory += heap.size;
            }
        }
        return totalDeviceLocalMemory;
    }

    public override string ToString() {
        var sb = new StringBuilder();
        var props = GetProperties();
        var mem = GetMemoryProperties();
        var apiVersion = new VkVersion(props.apiVersion);

        sb.AppendLine($"device name: {new VkUtf8String(props.deviceName)}");
        sb.AppendLine($"device type: {props.deviceType}");

        ulong totalGpuMemory = GetTotalGpuMemoryBytes();
        sb.AppendLine($"total gpu Memory: {FormatBytes(totalGpuMemory)}");

        sb.AppendLine($"vkapi version: {apiVersion.Major}.{apiVersion.Minor}.{apiVersion.Patch}");
#if DEBUG
        sb.AppendLine("memory heaps:");
        for (int i = 0; i < mem.memoryHeapCount; i++) {
            var heap = mem.memoryHeaps[i];
            sb.AppendLine($"heap {i}: size = {FormatBytes(heap.size)}, flags = {heap.flags}");
        }

        sb.AppendLine("Memory Types:");
        for (int i = 0; i < mem.memoryTypeCount; i++) {
            var type = mem.memoryTypes[i];
            sb.AppendLine($"type {i}: heap idx = {type.heapIndex}, prop flags = {type.propertyFlags}");
        }
#endif
        
        return sb.ToString();
    }
    
    private static string FormatBytes(ulong bytes) {
        const long gb = 1024L * 1024L * 1024L;
        const long mb = 1024L * 1024L;
        const long kb = 1024L;

        if (bytes >= gb) {
            return $"{bytes / (double)gb:0.00} GB";
        }
        else if (bytes >= mb) {
            return $"{bytes / (double)mb:0.00} MB";
        }
        else if (bytes >= kb) {
            return $"{bytes / (double)kb:0.00} KB";
        }
        else {
            return $"{bytes} B";
        }
    }
    
    public static bool operator ==(PhysicalDevice left, PhysicalDevice right) => left.Equals(right);
    public static bool operator !=(PhysicalDevice left, PhysicalDevice right) => !(left == right);
    public bool Equals(PhysicalDevice other) => Value.Equals(other.Value);
    public override bool Equals(object? obj) => obj is PhysicalDevice other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
}