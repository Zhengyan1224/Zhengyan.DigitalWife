using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

internal sealed class PmxShader : IDisposable
{
    private readonly GL _gl;

    public PmxShader(GL gl)
    {
        _gl = gl;
        Id = PmxShaderResources.CreateProgram(
            gl,
            PmxShaderResources.ModelVertexShader,
            PmxShaderResources.ModelFragmentShader);

        InPos = (uint)gl.GetAttribLocation(Id, "in_Pos");
        InNor = (uint)gl.GetAttribLocation(Id, "in_Nor");
        InUV = (uint)gl.GetAttribLocation(Id, "in_UV");

        UniWV = gl.GetUniformLocation(Id, "u_WV");
        UniWVP = gl.GetUniformLocation(Id, "u_WVP");
        UniAlpha = gl.GetUniformLocation(Id, "u_Alpha");
        UniDiffuse = gl.GetUniformLocation(Id, "u_Diffuse");
        UniAmbient = gl.GetUniformLocation(Id, "u_Ambient");
        UniSpecular = gl.GetUniformLocation(Id, "u_Specular");
        UniSpecularPower = gl.GetUniformLocation(Id, "u_SpecularPower");
        UniLightColor = gl.GetUniformLocation(Id, "u_LightColor");
        UniLightDir = gl.GetUniformLocation(Id, "u_LightDir");
        UniAmbientLightColor = gl.GetUniformLocation(Id, "u_AmbientLightColor");
        UniAmbientLightStrength = gl.GetUniformLocation(Id, "u_AmbientLightStrength");
        UniPointLightCount = gl.GetUniformLocation(Id, "u_PointLightCount");
        UniPointLightPositionRanges = Enumerable.Range(0, PointLightPacking.MaxLights)
            .Select(index => gl.GetUniformLocation(Id, $"u_PointLightPositionRange[{index}]"))
            .ToArray();
        UniPointLightColorIntensities = Enumerable.Range(0, PointLightPacking.MaxLights)
            .Select(index => gl.GetUniformLocation(Id, $"u_PointLightColorIntensity[{index}]"))
            .ToArray();
        UniSpotLightCount = gl.GetUniformLocation(Id, "u_SpotLightCount");
        UniSpotLightPositionRanges = Enumerable.Range(0, SpotLightPacking.MaxLights)
            .Select(index => gl.GetUniformLocation(Id, $"u_SpotLightPositionRange[{index}]"))
            .ToArray();
        UniSpotLightDirectionOuterCosines = Enumerable.Range(0, SpotLightPacking.MaxLights)
            .Select(index => gl.GetUniformLocation(Id, $"u_SpotLightDirectionOuterCosine[{index}]"))
            .ToArray();
        UniSpotLightColorIntensities = Enumerable.Range(0, SpotLightPacking.MaxLights)
            .Select(index => gl.GetUniformLocation(Id, $"u_SpotLightColorIntensity[{index}]"))
            .ToArray();
        UniSpotLightConeParameters = Enumerable.Range(0, SpotLightPacking.MaxLights)
            .Select(index => gl.GetUniformLocation(Id, $"u_SpotLightConeParameters[{index}]"))
            .ToArray();
        UniTexMode = gl.GetUniformLocation(Id, "u_TexMode");
        UniTex = gl.GetUniformLocation(Id, "u_Tex");
        UniTexMulFactor = gl.GetUniformLocation(Id, "u_TexMulFactor");
        UniTexAddFactor = gl.GetUniformLocation(Id, "u_TexAddFactor");
        UniToonTexMode = gl.GetUniformLocation(Id, "u_ToonTexMode");
        UniToonTex = gl.GetUniformLocation(Id, "u_ToonTex");
        UniToonTexMulFactor = gl.GetUniformLocation(Id, "u_ToonTexMulFactor");
        UniToonTexAddFactor = gl.GetUniformLocation(Id, "u_ToonTexAddFactor");
        UniSphereTexMode = gl.GetUniformLocation(Id, "u_SphereTexMode");
        UniSphereTex = gl.GetUniformLocation(Id, "u_SphereTex");
        UniSphereTexMulFactor = gl.GetUniformLocation(Id, "u_SphereTexMulFactor");
        UniSphereTexAddFactor = gl.GetUniformLocation(Id, "u_SphereTexAddFactor");
        UniShadowMap0 = gl.GetUniformLocation(Id, "u_ShadowMap0");
        UniShadowMap1 = gl.GetUniformLocation(Id, "u_ShadowMap1");
        UniShadowMap2 = gl.GetUniformLocation(Id, "u_ShadowMap2");
        UniShadowMap3 = gl.GetUniformLocation(Id, "u_ShadowMap3");
        UniShadowMapEnabled = gl.GetUniformLocation(Id, "u_ShadowMapEnabled");
        UniShadowMapStrength = gl.GetUniformLocation(Id, "u_ShadowMapStrength");
        UniShadowMapBias = gl.GetUniformLocation(Id, "u_ShadowMapBias");
        UniShadowMapTexelSize = gl.GetUniformLocation(Id, "u_ShadowMapTexelSize");
        UniLightWvp0 = gl.GetUniformLocation(Id, "u_LightWVP[0]");
        UniLightWvp1 = gl.GetUniformLocation(Id, "u_LightWVP[1]");
        UniLightWvp2 = gl.GetUniformLocation(Id, "u_LightWVP[2]");
        UniLightWvp3 = gl.GetUniformLocation(Id, "u_LightWVP[3]");
        UniShadowMapSplitPosition0 = gl.GetUniformLocation(Id, "u_ShadowMapSplitPositions[0]");
    }

    public uint Id { get; }

    public uint InPos { get; }

    public uint InNor { get; }

    public uint InUV { get; }

    public int UniWV { get; }

    public int UniWVP { get; }

    public int UniAlpha { get; }

    public int UniDiffuse { get; }

    public int UniAmbient { get; }

    public int UniSpecular { get; }

    public int UniSpecularPower { get; }

    public int UniLightColor { get; }

    public int UniLightDir { get; }

    public int UniAmbientLightColor { get; }

    public int UniAmbientLightStrength { get; }

    public int UniPointLightCount { get; }

    public int[] UniPointLightPositionRanges { get; }

    public int[] UniPointLightColorIntensities { get; }

    public int UniSpotLightCount { get; }

    public int[] UniSpotLightPositionRanges { get; }

    public int[] UniSpotLightDirectionOuterCosines { get; }

    public int[] UniSpotLightColorIntensities { get; }

    public int[] UniSpotLightConeParameters { get; }

    public int UniTexMode { get; }

    public int UniTex { get; }

    public int UniTexMulFactor { get; }

    public int UniTexAddFactor { get; }

    public int UniToonTexMode { get; }

    public int UniToonTex { get; }

    public int UniToonTexMulFactor { get; }

    public int UniToonTexAddFactor { get; }

    public int UniSphereTexMode { get; }

    public int UniSphereTex { get; }

    public int UniSphereTexMulFactor { get; }

    public int UniSphereTexAddFactor { get; }

    public int UniShadowMap0 { get; }

    public int UniShadowMap1 { get; }

    public int UniShadowMap2 { get; }

    public int UniShadowMap3 { get; }

    public int UniShadowMapEnabled { get; }

    public int UniShadowMapStrength { get; }

    public int UniShadowMapBias { get; }

    public int UniShadowMapTexelSize { get; }

    public int UniLightWvp0 { get; }

    public int UniLightWvp1 { get; }

    public int UniLightWvp2 { get; }

    public int UniLightWvp3 { get; }

    public int UniShadowMapSplitPosition0 { get; }

    public void Dispose()
    {
        _gl.DeleteProgram(Id);
        GC.SuppressFinalize(this);
    }
}

