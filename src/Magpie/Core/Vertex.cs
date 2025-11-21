using System.Numerics;
using System.Runtime.InteropServices;

namespace Magpie.Core;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct VertexPositionColor(in Vector3 position, in Vector4 color) {
    public static unsafe int SizeInBytes => sizeof(VertexPositionColor);

    public readonly Vector3 Position = position;
    public readonly Vector4 Color = color;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct VertexPositionTexture(in Vector3 position, in Vector2 texCoord) {
    public static unsafe int SizeInBytes => sizeof(VertexPositionTexture);

    public readonly Vector3 Position = position;
    public readonly Vector2 TexCoord = texCoord;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct VertexPositionColorTexture(in Vector3 position, in Vector4 color, in Vector2 texCoord) {
    public static unsafe int SizeInBytes => sizeof(VertexPositionColorTexture);

    public readonly Vector3 Position = position;
    public readonly Vector4 Color = color;
    public readonly Vector2 TexCoord = texCoord;
}