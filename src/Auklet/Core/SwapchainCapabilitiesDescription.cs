using Vortice.Vulkan;

namespace Auklet.Core;

public readonly unsafe ref struct SwapchainCapabilitiesDescription(
    VkSurfaceCapabilitiesKHR capabilities,
    ReadOnlySpan<VkSurfaceFormatKHR> formats,
    ReadOnlySpan<VkPresentModeKHR> presentModes) {
    
    public readonly VkSurfaceCapabilitiesKHR Capabilities = capabilities;
    public readonly ReadOnlySpan<VkSurfaceFormatKHR> Formats = formats;
    public readonly ReadOnlySpan<VkPresentModeKHR> PresentModes = presentModes; 
    
    public readonly VkPresentModeKHR ChooseSwapPresentMode() {
        foreach (VkPresentModeKHR availablePresentMode in PresentModes) {
            if (availablePresentMode == VkPresentModeKHR.Mailbox) {
                return availablePresentMode;
            }
        }

        return VkPresentModeKHR.Fifo;
    }
    
    public readonly VkSurfaceFormatKHR ChooseSwapSurfaceFormat() {
        if (Formats.Length == 1 && Formats[0].format == VkFormat.Undefined) 
            return new VkSurfaceFormatKHR(VkFormat.B8G8R8A8Unorm, Formats[0].colorSpace);

        foreach (VkSurfaceFormatKHR availableFormat in Formats) {
            if (availableFormat.format == VkFormat.B8G8R8A8Unorm) {
                return availableFormat;
            }
        }

        return Formats[0];
    }
}