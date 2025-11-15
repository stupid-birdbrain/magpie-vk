using System.Numerics;
using System.Runtime.InteropServices;

namespace Magpie.Core;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct VertexPositionColor(in Vector3 position, in Vector4 color) {
    public static unsafe int SizeInBytes => sizeof(VertexPositionColor);

    public readonly Vector3 Position = position;
    public readonly Vector4 Color = color;
}