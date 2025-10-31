using System.Diagnostics;
using Vortice.Vulkan;

namespace Magpie.Core;

/// <summary>
///     Contains info related to the vulkan library, such as version, name, global layers, etc.
/// </summary>
public sealed unsafe class VkCtx : IDisposable {
    public readonly VkVersion Version;

    private readonly string _name;
    
    public ReadOnlySpan<char> Name => _name.AsSpan();

    public VkCtx(string libName) {
        _name = libName;

        var result = Vulkan.vkInitialize(_name.Length == 0 ? null : _name);
        if(result != VkResult.Success) throw new VkException(result);

        Version = Vulkan.vkEnumerateInstanceVersion();
        if(Version < VkVersion.Version_1_1) throw new PlatformNotSupportedException("vulkan 1.1 or above is required.");
    }
        
    public string[] GetGlobalLayers() {
        uint count = 0;
        var result = Vulkan.vkEnumerateInstanceLayerProperties(&count, null);
        if(result != VkResult.Success) {
            throw new Exception("failed to enumerate instance layers!");
        }

        if (count > 0) {
            VkLayerProperties* properties = stackalloc VkLayerProperties[(int)count];
            result = Vulkan.vkEnumerateInstanceLayerProperties(&count, properties);
            ThrowIfFailedToEnumerateInstanceLayerProperties(result);

            string[] availableInstanceLayers = new string[(int)count];
            for (int i = 0; i < count; i++) {
                VkUtf8String name = new(properties[i].layerName);
                Debug.Assert(name != null);
                
                availableInstanceLayers[i] = name.ToString() ?? string.Empty;
            }

            return availableInstanceLayers;
        }
        else {
            return Array.Empty<string>();
        }
    }
    
    public string?[] GetGlobalExtensions() {
        uint count = 0;
        Vulkan.vkEnumerateInstanceExtensionProperties(&count, null);

        if (count > 0) {
            VkExtensionProperties* extensionProperties = stackalloc VkExtensionProperties[(int)count];
            Vulkan.vkEnumerateInstanceExtensionProperties(&count, extensionProperties);

            var availableInstanceExtensions = new string?[(int)count];
            for (var i = 0; i < count; i++) {
                var name = new VkUtf8String(extensionProperties[i].extensionName);
                availableInstanceExtensions[i] = name.ToString();
            }

            return availableInstanceExtensions;
        }
        else {
            return Array.Empty<string>();
        }
    }

    [Conditional("DEBUG")]
    internal void ThrowIfFailedToEnumerateInstanceLayerProperties(VkResult result) {
        if(result != VkResult.Success) throw new Exception($"failed to enumerate instance layers properties! {result}");
    }
    
    public void Dispose() {
        
    }
}