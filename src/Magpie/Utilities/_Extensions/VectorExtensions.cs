using System.Numerics;
using Vortice.Vulkan;

namespace Magpie.Utilities;

#if NET10_0_OR_GREATER
public static class Vec2Ext {
    extension(Vector2 vec) {
        public static VkFormat AsFormat() => VkFormat.R32G32Sfloat;
    }
}

public static class Vec3Ext {
    extension(Vector3 vec) {
        public static VkFormat AsFormat() => VkFormat.R32G32B32Sfloat;
    }
}

public static class Vec4Ext {
    extension(Vector4 vec) {
        public static VkFormat AsFormat() => VkFormat.R32G32B32A32Sfloat;
    }

}
#endif