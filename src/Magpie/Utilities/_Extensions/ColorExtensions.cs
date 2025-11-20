using Standard;
using Vortice.Vulkan;

namespace Magpie.Utilities;

public static class ColorExtensions {
    // extension(Color color) {
    //     public VkClearValue ToVkClearValue() {
    //         var vkClearColor = new VkClearColorValue(
    //             color.R / 255f,
    //             color.G / 255f,
    //             color.B / 255f,
    //             color.A / 255f
    //         );
    //
    //         return new VkClearValue {
    //             color = vkClearColor
    //         };
    //     }
    // }
    
    public static VkClearValue ToVkClearValue(this Color color) {
        var vkClearColor = new VkClearColorValue(
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f
        );

        return new VkClearValue {
            color = vkClearColor
        };
    }
}