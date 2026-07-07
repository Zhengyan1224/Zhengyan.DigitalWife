using System.Numerics;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public static unsafe class GLShaderExtensions
{
    public static uint CreateShader(this GL gl, GLEnum type, string shaderSource)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, shaderSource);
        gl.CompileShader(shader);

        gl.GetShader(shader, GLEnum.CompileStatus, out int status);
        string log = gl.GetShaderInfoLog(shader);
        if (status == 0)
        {
            throw new InvalidOperationException($"{type}: {log}");
        }

        return shader;
    }

    public static uint CreateShaderProgramFromSource(this GL gl, string vertexShaderSource, string fragmentShaderSource)
    {
        uint vertexShader = gl.CreateShader(GLEnum.VertexShader, vertexShaderSource);
        uint fragmentShader = gl.CreateShader(GLEnum.FragmentShader, fragmentShaderSource);

        uint program = gl.CreateProgram();
        gl.AttachShader(program, vertexShader);
        gl.AttachShader(program, fragmentShader);
        gl.LinkProgram(program);

        gl.GetProgram(program, GLEnum.LinkStatus, out int status);
        string log = gl.GetProgramInfoLog(program);
        if (status == 0)
        {
            throw new InvalidOperationException(log);
        }

        gl.DetachShader(program, vertexShader);
        gl.DetachShader(program, fragmentShader);
        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);

        return program;
    }

    public static void SetUniform(this GL gl, int location, int value) => gl.Uniform1(location, value);

    public static void SetUniform(this GL gl, int location, float value) => gl.Uniform1(location, value);

    public static void SetUniform(this GL gl, int location, Vector2 value) => gl.Uniform2(location, 1, (float*)&value);

    public static void SetUniform(this GL gl, int location, Vector3 value) => gl.Uniform3(location, 1, (float*)&value);

    public static void SetUniform(this GL gl, int location, Vector4 value) => gl.Uniform4(location, 1, (float*)&value);

    public static void SetUniform(this GL gl, int location, Matrix4x4 value) => gl.UniformMatrix4(location, 1, false, (float*)&value);
}

