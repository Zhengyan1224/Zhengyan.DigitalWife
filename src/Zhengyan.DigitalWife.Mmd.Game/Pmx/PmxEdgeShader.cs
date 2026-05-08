using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

internal sealed class PmxEdgeShader : IDisposable
{
    private readonly GL _gl;

    public PmxEdgeShader(GL gl)
    {
        _gl = gl;
        Id = PmxShaderResources.CreateProgram(
            gl,
            PmxShaderResources.EdgeVertexShader,
            PmxShaderResources.EdgeFragmentShader);

        InPos = (uint)gl.GetAttribLocation(Id, "in_Pos");
        InNor = (uint)gl.GetAttribLocation(Id, "in_Nor");
        UniWV = gl.GetUniformLocation(Id, "u_WV");
        UniWVP = gl.GetUniformLocation(Id, "u_WVP");
        UniScreenSize = gl.GetUniformLocation(Id, "u_ScreenSize");
        UniEdgeSize = gl.GetUniformLocation(Id, "u_EdgeSize");
        UniEdgeColor = gl.GetUniformLocation(Id, "u_EdgeColor");
    }

    public uint Id { get; }

    public uint InPos { get; }

    public uint InNor { get; }

    public int UniWV { get; }

    public int UniWVP { get; }

    public int UniScreenSize { get; }

    public int UniEdgeSize { get; }

    public int UniEdgeColor { get; }

    public void Dispose()
    {
        _gl.DeleteProgram(Id);
        GC.SuppressFinalize(this);
    }
}

