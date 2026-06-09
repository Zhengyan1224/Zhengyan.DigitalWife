using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

internal sealed class PmxShadowDepthShader : IDisposable
{
    private readonly GL _gl;

    public PmxShadowDepthShader(GL gl)
    {
        _gl = gl;
        Id = PmxShaderResources.CreateProgram(
            gl,
            PmxShaderResources.ShadowDepthVertexShader,
            PmxShaderResources.ShadowDepthFragmentShader);

        InPos = (uint)gl.GetAttribLocation(Id, "in_Pos");
        UniWorldLightViewProjection = gl.GetUniformLocation(Id, "u_WorldLightViewProjection");
    }

    public uint Id { get; }

    public uint InPos { get; }

    public int UniWorldLightViewProjection { get; }

    public void Dispose()
    {
        _gl.DeleteProgram(Id);
        GC.SuppressFinalize(this);
    }
}
