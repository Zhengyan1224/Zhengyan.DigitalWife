using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public enum CustomShaderUniformType
{
    Float,
    Int,
    Vector2,
    Vector3,
    Vector4
}

public readonly record struct CustomShaderUniformValue(CustomShaderUniformType Type, Vector4 Value)
{
    public static CustomShaderUniformValue FromFloat(float value) => new(CustomShaderUniformType.Float, new Vector4(value, 0.0f, 0.0f, 0.0f));

    public static CustomShaderUniformValue FromInt(int value) => new(CustomShaderUniformType.Int, new Vector4(value, 0.0f, 0.0f, 0.0f));

    public static CustomShaderUniformValue FromVector2(float x, float y) => new(CustomShaderUniformType.Vector2, new Vector4(x, y, 0.0f, 0.0f));

    public static CustomShaderUniformValue FromVector3(float x, float y, float z) => new(CustomShaderUniformType.Vector3, new Vector4(x, y, z, 0.0f));

    public static CustomShaderUniformValue FromVector4(float x, float y, float z, float w) => new(CustomShaderUniformType.Vector4, new Vector4(x, y, z, w));
}
