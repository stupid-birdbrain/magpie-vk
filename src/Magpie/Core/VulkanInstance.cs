using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Vortice.Vulkan;

namespace Magpie.Core;

public unsafe struct VulkanInstance {
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
    
    public readonly VkDebugUtilsMessengerEXT debugMessenger;

    internal VkInstance Value;

    public readonly nint Address => Value.Handle;
    
    public VulkanInstance(VkCtx libraryContext, string appName, string engineName) {
        List<string> inputLayers = new();

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

        VkDebugUtilsMessengerCreateInfoEXT debugUtilsCreateInfo = new();
        
        if (inputLayers.Count > 0) {
            instanceCreateInfo.enabledLayerCount = (uint)inputLayers.Count;
            instanceCreateInfo.ppEnabledLayerNames = new VkStringArray(inputLayers.ToArray());
        }
        
        var result = Vulkan.vkCreateInstance(&instanceCreateInfo, null, out Value);
        ThrowIfInstanceCreationFailed(result);
        
        Console.WriteLine(new VkStringArray(inputLayers.ToArray()));
        
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
        if (messageSeverity == VkDebugUtilsMessageSeverityFlagsEXT.Error) {
            throw new Exception(message);
        }

        if (messageTypes == VkDebugUtilsMessageTypeFlagsEXT.Validation) {
            Console.WriteLine($"Validation: {messageSeverity} | {message}");
        }
        else {
            Console.WriteLine($"{messageSeverity} - {message}");
        }

        switch(messageSeverity) {
            case VkDebugUtilsMessageSeverityFlagsEXT.Error:
                Console.ForegroundColor = ConsoleColor.DarkRed;
                break;
            case VkDebugUtilsMessageSeverityFlagsEXT.Warning:
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                break;
            case VkDebugUtilsMessageSeverityFlagsEXT.Info:
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                break;
            case VkDebugUtilsMessageSeverityFlagsEXT.Verbose:
                Console.ForegroundColor = ConsoleColor.White;
                break;
        }

        return Vulkan.VK_FALSE;
    }
}