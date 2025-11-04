using Magpie.Graphics;
using Magpie.Utilities;
using SDL3;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Magpie.Core;

/// <summary>
///     Represents a vulkan instance, managing its creation and instance level layers/extensions.
/// </summary>
public unsafe struct VulkanInstance : IDisposable {
    private static readonly string[] preferred_validation_layers = [
        "VK_LAYER_KHRONOS_validation"
    ];

    private static readonly string[] fallback_validation_layers = [
        "VK_LAYER_LUNARG_standard_validation"
    ];

    private static readonly string[] fallback_individual_layers = [
        "VK_LAYER_GOOGLE_threading",
        "VK_LAYER_LUNARG_parameter_validation",
        "VK_LAYER_LUNARG_object_tracker",
        "VK_LAYER_LUNARG_core_validation",
        "VK_LAYER_GOOGLE_unique_objects",
    ];

    private static readonly string[] fallback_fallback_layers = [
        "VK_LAYER_LUNARG_core_validation"
    ];

    public VkDebugUtilsMessengerEXT DebugMessenger;

    internal VkInstance Value;

    public readonly nint Address => Value.Handle;
    
    private readonly VkStringArray _enabledLayerNamesArray;
    private readonly VkStringArray _enabledExtensionNamesArray;

    private List<PhysicalDevice> _devices;
    
    public VulkanInstance(VkCtx libraryContext, string appName, string engineName) {
        List<string> inputLayers = new();
#if DEBUG
        var globalLayers = libraryContext.GetGlobalLayers();

        if (containsAll(globalLayers, preferred_validation_layers))
            inputLayers.AddRange(preferred_validation_layers);
        else if (containsAll(globalLayers, fallback_validation_layers))
            inputLayers.AddRange(fallback_validation_layers);
        else if (containsAll(globalLayers, fallback_individual_layers))
            inputLayers.AddRange(fallback_individual_layers);
        else if (containsAll(globalLayers, fallback_fallback_layers))
            inputLayers.AddRange(fallback_fallback_layers);
        else {
            if (globalLayers.Length > 0) {
                Console.WriteLine("No suitable validation layers found, there were instead: " + string.Join(", ", globalLayers));
            }
            else {
                Console.WriteLine("No global layers found.");
            }
        }
#endif
        
        List<string?> requiredExtensions = new();

        var sdlRequiredExtensions = SDL.VulkanGetInstanceExtensions(out uint _);
        if (sdlRequiredExtensions != null) {
            requiredExtensions.AddRange(sdlRequiredExtensions);
        }
        
        if (inputLayers.Any()) {
            if (!requiredExtensions.Contains(new VkUtf8String(Vulkan.VK_EXT_DEBUG_UTILS_EXTENSION_NAME.GetPointer()).ToString())) {
                requiredExtensions.Add(new VkUtf8String(Vulkan.VK_EXT_DEBUG_UTILS_EXTENSION_NAME.GetPointer()).ToString());
            }
        }
        
        var appInfo = new VkApplicationInfo {
            sType = VkStructureType.ApplicationInfo,
            pApplicationName = new VkUtf8ReadOnlyString(Encoding.UTF8.GetBytes(appName)),
            applicationVersion = VkVersion.Version_1_0,
            pEngineName = new VkUtf8ReadOnlyString(Encoding.UTF8.GetBytes(engineName)),
            engineVersion = VkVersion.Version_1_0,
            apiVersion = libraryContext.Version,
        };
        
        var instanceCreateInfo = new VkInstanceCreateInfo {
            sType = VkStructureType.InstanceCreateInfo,
            pApplicationInfo = &appInfo,
            enabledLayerCount = (uint)inputLayers.Count,
            ppEnabledLayerNames = new VkStringArray(inputLayers)
        };

        if (inputLayers.Any()) {
            _enabledLayerNamesArray = new VkStringArray(inputLayers.ToArray());
            instanceCreateInfo.enabledLayerCount = _enabledLayerNamesArray.Length;
            instanceCreateInfo.ppEnabledLayerNames = _enabledLayerNamesArray;
        }
        
        VkStringArray enabledExtensionNamesArray = new(requiredExtensions.ToArray()!);
        instanceCreateInfo.enabledExtensionCount = enabledExtensionNamesArray.Length;
        instanceCreateInfo.ppEnabledExtensionNames = enabledExtensionNamesArray;


        VkDebugUtilsMessengerCreateInfoEXT debugUtilsCreateInfo = new();
        if (inputLayers.Any()) {
            PopulateDebugMessengerCreateInfo(ref debugUtilsCreateInfo);
            instanceCreateInfo.pNext = &debugUtilsCreateInfo;
        }
        
        var result = Vulkan.vkCreateInstance(&instanceCreateInfo, null, out Value);
        ThrowIfInstanceCreationFailed(result);
        
        Vulkan.vkLoadInstanceOnly(Value);
        
        if (inputLayers.Any()) {
            VkResult createMessengerResult = Vulkan.vkCreateDebugUtilsMessengerEXT(Value, debugUtilsCreateInfo, null, out VkDebugUtilsMessengerEXT createdMessenger);
            ThrowIfDebugMessengerCreationFailed(createMessengerResult);
            DebugMessenger = createdMessenger;
        }
        else {
            DebugMessenger = VkDebugUtilsMessengerEXT.Null;
        }
        #if DEBUG
        Console.ForegroundColor =  ConsoleColor.Cyan;
        Console.WriteLine($"MAGPIE: created magpievk instance, app context: {appName}, {engineName}, {Address.ToString("X")}");
        Console.ForegroundColor =  ConsoleColor.Gray;
        
        Console.WriteLine($"enabled layers: [{string.Join(", ", inputLayers)}]");
        Console.WriteLine($"enabled extensions: [{string.Join(", ", requiredExtensions)}]");
        #endif
          
        
        uint physicalDeviceCount = 0;
        vkEnumeratePhysicalDevices(Value, &physicalDeviceCount, null);

        _devices = new((int)physicalDeviceCount);
        var physicalDevicesPointer = stackalloc VkPhysicalDevice[(int)physicalDeviceCount];
        result = vkEnumeratePhysicalDevices(Value, &physicalDeviceCount, physicalDevicesPointer);
        
        if(result != VkResult.Success) throw new Exception($"failed to enumerate valid physical device contestants! {result}");

        for (int i = 0; i < physicalDeviceCount; i++) {
            _devices.Add(new(physicalDevicesPointer[i]));
        }
        
        Console.WriteLine($"physical device count: {_devices.Count}");
        
        static bool containsAll(ReadOnlySpan<string> a, ReadOnlySpan<string> b) {
            foreach (string layer in b) {
                bool contains = false;
                foreach (string availableLayer in a) {
                    if (availableLayer == layer) {
                        contains = true;
                        break;
                    }
                }

                if (!contains) return false;
            }

            return true;
        }
    }
    
    [Conditional("DEBUG")]
    internal void ThrowIfInstanceCreationFailed(VkResult result) {
        if(result != VkResult.Success) throw new Exception($"failed to create vulkan instance! {result}");
    }
    
    [Conditional("DEBUG")]
    internal void ThrowIfDebugMessengerCreationFailed(VkResult result) {
        if(result != VkResult.Success) throw new Exception($"failed to create vulkan debug messenger! {result}");
    }
    
    private static void PopulateDebugMessengerCreateInfo(ref VkDebugUtilsMessengerCreateInfoEXT createInfo) {
        createInfo.sType = VkStructureType.DebugUtilsMessengerCreateInfoEXT;
        createInfo.messageSeverity = VkDebugUtilsMessageSeverityFlagsEXT.Verbose |
                                     VkDebugUtilsMessageSeverityFlagsEXT.Warning |
                                     VkDebugUtilsMessageSeverityFlagsEXT.Error;
        createInfo.messageType = VkDebugUtilsMessageTypeFlagsEXT.General |
                                 VkDebugUtilsMessageTypeFlagsEXT.Performance |
                                 VkDebugUtilsMessageTypeFlagsEXT.Validation;
        createInfo.pfnUserCallback = &DebugMessengerCallback;
    }
    
    
    [UnmanagedCallersOnly]
    private static uint DebugMessengerCallback(VkDebugUtilsMessageSeverityFlagsEXT messageSeverity, VkDebugUtilsMessageTypeFlagsEXT messageTypes, VkDebugUtilsMessengerCallbackDataEXT* pCallbackData, void* userData) {
        var message = new string((sbyte*)pCallbackData->pMessage);

        switch(messageSeverity) {
            case VkDebugUtilsMessageSeverityFlagsEXT.Error: Console.ForegroundColor = ConsoleColor.Red;
                break;
            case VkDebugUtilsMessageSeverityFlagsEXT.Warning: Console.ForegroundColor = ConsoleColor.Yellow;
                break;
            case VkDebugUtilsMessageSeverityFlagsEXT.Info: Console.ForegroundColor = ConsoleColor.Green;
                break;
            case VkDebugUtilsMessageSeverityFlagsEXT.Verbose: Console.ForegroundColor = ConsoleColor.White;
                break;
            default: Console.ForegroundColor = ConsoleColor.Gray;
                break;
        }
        
        Console.WriteLine(messageTypes == VkDebugUtilsMessageTypeFlagsEXT.Validation
            ? $"Validation: {messageSeverity} | {message}"
            : $"{messageSeverity} - {message}");

        return VK_FALSE;
    }

    public readonly bool TryGetBestPhysicalDevice(ReadOnlySpan<string> requiredExtensions, out PhysicalDevice device) {
        uint highestScore = 0;
        device = default;
        
        for (int i = 0; i < _devices.Count; i++) {
            var score = getScore(_devices[i], requiredExtensions);
            
            if (score > highestScore) {
                highestScore = score;
                device = _devices[i];
            }
        }

        return device != default;

        static unsafe uint getScore(PhysicalDevice physicalDevice, ReadOnlySpan<string> requiredExtensions) {
            if (!physicalDevice.TryGetGraphicsQueueFamily(out _)) {
                return 0;
            }

            var availableExtensions = physicalDevice.GetExtensions();
            if (availableExtensions.Length > 0) {
                foreach (var requiredExtension in requiredExtensions) {
                    bool isAvailable = false;
                    foreach (VkExtensionProperties extension in availableExtensions) {
                        var extensionName = Marshal.PtrToStringAnsi((IntPtr)extension.extensionName);
                        if (extensionName == requiredExtension) {
                            isAvailable = true;
                            break;
                        }
                    }

                    if (!isAvailable) {
                        return 0;
                    }
                }
            }
            else if (requiredExtensions.Length > 0) {
                return 0;
            }

            var props = physicalDevice.GetProperties();
            uint score = props.limits.maxImageDimension2D;
            
            if (props.deviceType == VkPhysicalDeviceType.DiscreteGpu) {
                score *= 1024;
            }

            return score;
        }
    }
    
    public void Dispose() {
        if(DebugMessenger != default(VkDebugUtilsMessengerEXT)) {
            vkDestroyDebugUtilsMessengerEXT(this, DebugMessenger);
        }
        
        vkDestroyInstance(this);
        Value = default;
    }
    
    public static implicit operator VkInstance(VulkanInstance instance) => instance.Value;
    public static implicit operator nint(VulkanInstance instance) => instance.Address;
}