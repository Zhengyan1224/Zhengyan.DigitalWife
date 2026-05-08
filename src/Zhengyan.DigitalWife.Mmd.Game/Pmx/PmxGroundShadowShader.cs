using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

internal sealed class PmxGroundShadowShader : IDisposable
{
    private readonly GL _gl;

    public PmxGroundShadowShader(GL gl)
    {
        _gl = gl;
        Id = PmxShaderResources.CreateProgram(
            gl,
            PmxShaderResources.GroundShadowVertexShader,
            PmxShaderResources.GroundShadowFragmentShader);

        InPos = (uint)gl.GetAttribLocation(Id, "in_Pos");
        UniWVP = gl.GetUniformLocation(Id, "u_WVP");
        UniShadowColor = gl.GetUniformLocation(Id, "u_ShadowColor");
    }

    public uint Id { get; }

    public uint InPos { get; }

    public int UniWVP { get; }

    public int UniShadowColor { get; }

    public void Dispose()
    {
        _gl.DeleteProgram(Id);
        GC.SuppressFinalize(this);
    }
}

