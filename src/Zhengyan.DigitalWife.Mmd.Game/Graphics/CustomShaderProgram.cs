using System.Numerics;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public sealed class CustomShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _uniformLocations = new(StringComparer.Ordinal);

    public CustomShaderProgram(GL gl, string vertexShaderPath, string fragmentShaderPath)
    {
        _gl = gl;
        VertexShaderPath = ValidateShaderPath(vertexShaderPath, nameof(vertexShaderPath));
        FragmentShaderPath = ValidateShaderPath(fragmentShaderPath, nameof(fragmentShaderPath));

        string vertexShaderSource = File.ReadAllText(VertexShaderPath);
        string fragmentShaderSource = File.ReadAllText(FragmentShaderPath);
        Id = gl.CreateShaderProgramFromSource(vertexShaderSource, fragmentShaderSource);

        InPos = gl.GetAttribLocation(Id, "in_Pos");
        InNor = gl.GetAttribLocation(Id, "in_Nor");
        InUv = gl.GetAttribLocation(Id, "in_Uv");
        InUV = gl.GetAttribLocation(Id, "in_UV");
    }

    public uint Id { get; }

    public string VertexShaderPath { get; }

    public string FragmentShaderPath { get; }

    public int InPos { get; }

    public int InNor { get; }

    public int InUv { get; }

    public int InUV { get; }

    public void SetUniform(string name, int value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            _gl.SetUniform(location, value);
        }
    }

    public void SetUniform(string name, float value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            _gl.SetUniform(location, value);
        }
    }

    public void SetUniform(string name, Vector2 value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            _gl.SetUniform(location, value);
        }
    }

    public void SetUniform(string name, Vector3 value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            _gl.SetUniform(location, value);
        }
    }

    public void SetUniform(string name, Vector4 value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            _gl.SetUniform(location, value);
        }
    }

    public void SetUniform(string name, Matrix4x4 value)
    {
        int location = GetUniformLocation(name);
        if (location >= 0)
        {
            _gl.SetUniform(location, value);
        }
    }

    public void ApplyUniforms(IReadOnlyDictionary<string, CustomShaderUniformValue> uniforms)
    {
        foreach ((string name, CustomShaderUniformValue uniform) in uniforms)
        {
            switch (uniform.Type)
            {
                case CustomShaderUniformType.Float:
                    SetUniform(name, uniform.Value.X);
                    break;
                case CustomShaderUniformType.Int:
                    SetUniform(name, (int)uniform.Value.X);
                    break;
                case CustomShaderUniformType.Vector2:
                    SetUniform(name, new Vector2(uniform.Value.X, uniform.Value.Y));
                    break;
                case CustomShaderUniformType.Vector3:
                    SetUniform(name, new Vector3(uniform.Value.X, uniform.Value.Y, uniform.Value.Z));
                    break;
                case CustomShaderUniformType.Vector4:
                    SetUniform(name, uniform.Value);
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (Id != 0)
        {
            _gl.DeleteProgram(Id);
        }

        GC.SuppressFinalize(this);
    }

    private int GetUniformLocation(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return -1;
        }

        if (!_uniformLocations.TryGetValue(name, out int location))
        {
            location = _gl.GetUniformLocation(Id, name);
            _uniformLocations.Add(name, location);
        }

        return location;
    }

    private static string ValidateShaderPath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Shader path cannot be empty.", parameterName);
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Shader file not found.", fullPath);
        }

        return fullPath;
    }
}
