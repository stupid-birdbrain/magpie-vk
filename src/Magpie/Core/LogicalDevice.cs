using Magpie.Graphics;
using System.Runtime.CompilerServices;
using Vortice.Vulkan;

namespace Magpie.Core;

public unsafe struct LogicalDevice : IDisposable {
    public readonly PhysicalDevice PhysicalDevice;

    internal VkDevice Value;
    public readonly nint Address => Value.Handle;
    
    private static readonly string[] device_layers = {
        "VK_LAYER_KHRONOS_validation"
    };

    public LogicalDevice(PhysicalDevice physicalDevice, ReadOnlySpan<uint> queueFamilies, ReadOnlySpan<string> deviceExtensions) {
        PhysicalDevice = physicalDevice;
        float priority = 1f;
        Span<VkDeviceQueueCreateInfo> queueCreateInfos = stackalloc VkDeviceQueueCreateInfo[queueFamilies.Length];
        
        for (int i = 0; i < queueFamilies.Length; i++) {
            uint queueFamily = queueFamilies[i];
            VkDeviceQueueCreateInfo queueCreateInfo = new() {
                sType = VkStructureType.DeviceQueueCreateInfo,
                queueFamilyIndex = queueFamily,
                queueCount = 1,
                pQueuePriorities = &priority
            };
            queueCreateInfos[i] = queueCreateInfo;
        }

        VkPhysicalDeviceFeatures features = new();
        features.samplerAnisotropy = true; 

        VkStringArray deviceExtensionNames = default;
        if (deviceExtensions.Length > 0) {
            deviceExtensionNames = new VkStringArray(deviceExtensions.ToArray());
        }

        VkDeviceCreateInfo createInfo = new() {
            sType = VkStructureType.DeviceCreateInfo,
            queueCreateInfoCount = (uint)queueCreateInfos.Length,
            pQueueCreateInfos = (VkDeviceQueueCreateInfo*)Unsafe.AsPointer(ref queueCreateInfos.GetPinnableReference()),
            enabledExtensionCount = deviceExtensionNames.Length,
            ppEnabledExtensionNames = deviceExtensionNames,
            pEnabledFeatures = &features,
        };

#if DEBUG
        VkStringArray deviceLayerNames = new(device_layers);
        createInfo.enabledLayerCount = deviceLayerNames.Length;
        createInfo.ppEnabledLayerNames = deviceLayerNames;
#endif

        var result = Vulkan.vkCreateDevice(physicalDevice.Value, &createInfo, null, out Value);

        Vulkan.vkLoadDevice(Value);
    }
    
    public void Dispose() {
        if (Value != VkDevice.Null) {
            Vulkan.vkDestroyDevice(Value, null);
            Value = VkDevice.Null;
        }
    }
    
    public static implicit operator VkDevice(LogicalDevice device) => device.Value;
    public static implicit operator nint(LogicalDevice device) => device.Address;
}