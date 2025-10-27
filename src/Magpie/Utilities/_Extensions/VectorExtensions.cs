using System.Numerics;
using Vortice.Vulkan;

namespace Magpie.Utilities;

public static class VectorExtensions {
    public static Vector2 AsVector2(this VkExtent2D vec) => new Vector2(vec.width, vec.height);
    
    public static VkFormat GetFormat(this Vector2 vec) => VkFormat.R32G32Sfloat;
    public static VkFormat GetFormat(this Vector3 vec) => VkFormat.R32G32B32Sfloat;
    public static VkFormat GetFormat(this Vector4 vec) => VkFormat.R32G32B32A32Sfloat;
}