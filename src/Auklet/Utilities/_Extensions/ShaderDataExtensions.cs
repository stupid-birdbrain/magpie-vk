using ShaderCompilation.Models;
using System.Numerics;

namespace Magpie.Utilities;

public static class ShaderDataExtensions {
    public static Type ToCSharpType(this ShaderDataType dataType) {
        return dataType switch {
            ShaderDataType.Float => typeof(float),
            ShaderDataType.Vector2 => typeof(Vector2),
            ShaderDataType.Vector3 => typeof(Vector3),
            ShaderDataType.Vector4 => typeof(Vector4),
            ShaderDataType.Matrix3x3 => typeof(Matrix4x4),
            ShaderDataType.Matrix4x4 => typeof(Matrix4x4),
            ShaderDataType.Int => typeof(int),
            ShaderDataType.UInt => typeof(uint),
            ShaderDataType.Bool => typeof(bool),
            ShaderDataType.Sampler2D => typeof(int), //             ?
            _ => typeof(void),
        };
    }
}