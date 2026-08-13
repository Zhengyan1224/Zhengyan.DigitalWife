using System.Numerics;
using System.Runtime.InteropServices;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

/// <summary>Owns backend-neutral buffers and descriptor inputs shared by PMX draw passes.</summary>
internal sealed unsafe class PmxGpuResources : IDisposable
{
    public PmxGpuResources(GraphicsDevice graphicsDevice, Zhengyan.DigitalWife.Mmd.MMDModel model)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(model);

        int vertexCount = model.GetVertexCount();
        int indexCount = model.GetIndexCount();
        PositionBuffer = graphicsDevice.CreateBuffer(new GpuBufferDescription(
            checked((uint)(sizeof(Vector3) * vertexCount)), GpuBufferKind.Vertex, Dynamic: true));
        NormalBuffer = graphicsDevice.CreateBuffer(new GpuBufferDescription(
            checked((uint)(sizeof(Vector3) * vertexCount)), GpuBufferKind.Vertex, Dynamic: true));
        UvBuffer = graphicsDevice.CreateBuffer(new GpuBufferDescription(
            checked((uint)(sizeof(Vector2) * vertexCount)), GpuBufferKind.Vertex, Dynamic: true));
        IndexBuffer = graphicsDevice.CreateBuffer(new GpuBufferDescription(
            checked((uint)(sizeof(uint) * indexCount)), GpuBufferKind.Index));
        FrameUniformBuffer = graphicsDevice.CreateBuffer(new GpuBufferDescription(
            (uint)Marshal.SizeOf<PmxFrameUniformData>(), GpuBufferKind.Uniform, Dynamic: true));
        MaterialUniformBuffer = graphicsDevice.CreateBuffer(new GpuBufferDescription(
            (uint)Marshal.SizeOf<PmxMaterialUniformData>(), GpuBufferKind.Uniform, Dynamic: true));
        EdgeUniformBuffer = graphicsDevice.CreateBuffer(new GpuBufferDescription(
            (uint)Marshal.SizeOf<PmxEdgeUniformData>(), GpuBufferKind.Uniform, Dynamic: true));
        GroundShadowUniformBuffer = graphicsDevice.CreateBuffer(new GpuBufferDescription(
            (uint)Marshal.SizeOf<PmxGroundShadowUniformData>(), GpuBufferKind.Uniform, Dynamic: true));
        ShadowDepthUniformBuffer = graphicsDevice.CreateBuffer(new GpuBufferDescription(
            (uint)Marshal.SizeOf<PmxShadowDepthUniformData>(), GpuBufferKind.Uniform, Dynamic: true));
        TextureSampler = graphicsDevice.CreateSampler(new GpuSamplerDescription());
        ToonTextureSampler = graphicsDevice.CreateSampler(new GpuSamplerDescription(
            GpuSamplerAddressMode.ClampToEdge,
            GpuSamplerAddressMode.ClampToEdge));
        DefaultTexture = graphicsDevice.CreateTexture2D();
        DefaultTexture.Fill(255, 255, 255, 255);
        IndexBuffer.Update(new ReadOnlySpan<uint>(model.GetIndices(), indexCount));
    }

    public IGpuBuffer PositionBuffer { get; }
    public IGpuBuffer NormalBuffer { get; }
    public IGpuBuffer UvBuffer { get; }
    public IGpuBuffer IndexBuffer { get; }
    public IGpuBuffer FrameUniformBuffer { get; }
    public IGpuBuffer MaterialUniformBuffer { get; }
    public IGpuBuffer EdgeUniformBuffer { get; }
    public IGpuBuffer GroundShadowUniformBuffer { get; }
    public IGpuBuffer ShadowDepthUniformBuffer { get; }
    public IGpuSampler TextureSampler { get; }
    public IGpuSampler ToonTextureSampler { get; }
    public ITexture2D DefaultTexture { get; }

    public PmxMaterialDescriptorSet CreateMaterialDescriptorSet(
        ITexture2D? baseTexture,
        ITexture2D? sphereTexture,
        ITexture2D? toonTexture)
    {
        return new PmxMaterialDescriptorSet(
            baseTexture ?? DefaultTexture,
            sphereTexture ?? DefaultTexture,
            toonTexture ?? DefaultTexture,
            TextureSampler,
            ToonTextureSampler);
    }

    public void UploadFrameUniforms(in PmxFrameUniformData data)
    {
        PmxFrameUniformData value = data;
        FrameUniformBuffer.Update(MemoryMarshal.CreateReadOnlySpan(ref value, 1));
    }

    public void UploadMaterialUniforms(in PmxMaterialUniformData data)
    {
        PmxMaterialUniformData value = data;
        MaterialUniformBuffer.Update(MemoryMarshal.CreateReadOnlySpan(ref value, 1));
    }

    public void UploadPose(Zhengyan.DigitalWife.Mmd.MMDModel model, bool uploadUv)
    {
        int vertexCount = model.GetVertexCount();
        PositionBuffer.Update(new ReadOnlySpan<Vector3>(model.GetUpdatePositions(), vertexCount));
        NormalBuffer.Update(new ReadOnlySpan<Vector3>(model.GetUpdateNormals(), vertexCount));
        if (uploadUv)
        {
            UvBuffer.Update(new ReadOnlySpan<Vector2>(model.GetUpdateUVs(), vertexCount));
        }
    }

    public void UploadUv(Zhengyan.DigitalWife.Mmd.MMDModel model)
    {
        int vertexCount = model.GetVertexCount();
        UvBuffer.Update(new ReadOnlySpan<Vector2>(model.GetUpdateUVs(), vertexCount));
    }

    public void Dispose()
    {
        PositionBuffer.Dispose();
        NormalBuffer.Dispose();
        UvBuffer.Dispose();
        IndexBuffer.Dispose();
        FrameUniformBuffer.Dispose();
        MaterialUniformBuffer.Dispose();
        EdgeUniformBuffer.Dispose();
        GroundShadowUniformBuffer.Dispose();
        ShadowDepthUniformBuffer.Dispose();
        TextureSampler.Dispose();
        ToonTextureSampler.Dispose();
        DefaultTexture.Dispose();
        GC.SuppressFinalize(this);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PmxFrameUniformData
    {
        public Matrix4x4 World;
        public Matrix4x4 View;
        public Matrix4x4 Projection;
        public Matrix4x4 WorldViewProjection;
        public Vector4 LightColor;
        public Vector4 LightDirection;
        public Vector4 AmbientLightColor;
        public Vector4 Parameters;
        public Matrix4x4 ShadowLightViewProjection;
        public Vector4 ShadowParameters;
        public Vector4 PointLightMeta;
        public fixed float PointLightPositionRanges[PointLightPacking.MaxLights * 4];
        public fixed float PointLightColorIntensities[PointLightPacking.MaxLights * 4];
        public Vector4 SpotLightMeta;
        public fixed float SpotLightPositionRanges[SpotLightPacking.MaxLights * 4];
        public fixed float SpotLightDirectionOuterCosines[SpotLightPacking.MaxLights * 4];
        public fixed float SpotLightColorIntensities[SpotLightPacking.MaxLights * 4];
        public fixed float SpotLightConeParameters[SpotLightPacking.MaxLights * 4];
        public Vector4 LocalShadowMeta;
        public Vector4 LocalShadowAtlasParameters;
        public Matrix4x4 LocalShadowInverseView;
        public fixed float PointLightShadowMeta[LocalLightShadowLimits.MaxShadowedPointLights * 4];
        public fixed float SpotLightShadowMeta[LocalLightShadowLimits.MaxShadowedSpotLights * 4];
        public fixed float PointLightShadowMatrices[LocalLightShadowLimits.MaxPointShadowFaces * 16];
        public fixed float PointLightShadowAtlasRects[LocalLightShadowLimits.MaxPointShadowFaces * 4];
        public fixed float SpotLightShadowMatrices[LocalLightShadowLimits.MaxShadowedSpotLights * 16];
        public fixed float SpotLightShadowAtlasRects[LocalLightShadowLimits.MaxShadowedSpotLights * 4];
    }

    public static void SetLocalLightShadows(
        ref PmxFrameUniformData data,
        LocalLightShadowBinding? binding,
        Matrix4x4 view)
    {
        Span<Vector4> pointMeta = stackalloc Vector4[LocalLightShadowLimits.MaxShadowedPointLights];
        Span<Vector4> spotMeta = stackalloc Vector4[LocalLightShadowLimits.MaxShadowedSpotLights];
        Span<Matrix4x4> pointMatrices = stackalloc Matrix4x4[LocalLightShadowLimits.MaxPointShadowFaces];
        Span<Vector4> pointRects = stackalloc Vector4[LocalLightShadowLimits.MaxPointShadowFaces];
        Span<Matrix4x4> spotMatrices = stackalloc Matrix4x4[LocalLightShadowLimits.MaxShadowedSpotLights];
        Span<Vector4> spotRects = stackalloc Vector4[LocalLightShadowLimits.MaxShadowedSpotLights];
        pointMeta.Fill(new Vector4(-1.0f, 0.0f, 0.0f, 0.0f));
        spotMeta.Fill(new Vector4(-1.0f, 0.0f, 0.0f, 0.0f));
        pointMatrices.Clear();
        pointRects.Clear();
        spotMatrices.Clear();
        spotRects.Clear();

        if (binding is not null && Matrix4x4.Invert(view, out Matrix4x4 inverseView))
        {
            for (int slot = 0; slot < binding.PointLights.Count && slot < LocalLightShadowLimits.MaxShadowedPointLights; slot++)
            {
                PointLightShadowBinding light = binding.PointLights[slot];
                pointMeta[slot] = new Vector4(
                    light.PackedLightIndex,
                    light.NearPlane,
                    light.FarPlane,
                    0.0f);
                for (int face = 0; face < LocalLightShadowLimits.PointFacesPerLight; face++)
                {
                    int index = slot * LocalLightShadowLimits.PointFacesPerLight + face;
                    if (face < light.FaceViewProjections.Count)
                        pointMatrices[index] = inverseView * light.FaceViewProjections[face];
                    if (face < light.AtlasRects.Count)
                        pointRects[index] = light.AtlasRects[face];
                }
            }

            for (int slot = 0; slot < binding.SpotLights.Count && slot < LocalLightShadowLimits.MaxShadowedSpotLights; slot++)
            {
                SpotLightShadowBinding light = binding.SpotLights[slot];
                spotMeta[slot] = new Vector4(
                    light.PackedLightIndex,
                    light.NearPlane,
                    light.FarPlane,
                    0.0f);
                spotMatrices[slot] = inverseView * light.LightViewProjection;
                spotRects[slot] = light.AtlasRect;
            }

            data.LocalShadowMeta = new Vector4(
                Math.Min(binding.PointLights.Count, LocalLightShadowLimits.MaxShadowedPointLights),
                Math.Min(binding.SpotLights.Count, LocalLightShadowLimits.MaxShadowedSpotLights),
                Math.Clamp(binding.Strength, 0.0f, 1.0f),
                Math.Max(binding.Bias, 0.0f));
            data.LocalShadowAtlasParameters = new Vector4(
                binding.TexelSize,
                Math.Max(binding.NormalOffset, 0.0f),
                0.0f);
            data.LocalShadowInverseView = inverseView;
        }

        fixed (float* pointMetaDestination = data.PointLightShadowMeta)
        fixed (float* spotMetaDestination = data.SpotLightShadowMeta)
        fixed (float* pointMatrixDestination = data.PointLightShadowMatrices)
        fixed (float* pointRectDestination = data.PointLightShadowAtlasRects)
        fixed (float* spotMatrixDestination = data.SpotLightShadowMatrices)
        fixed (float* spotRectDestination = data.SpotLightShadowAtlasRects)
        {
            pointMeta.CopyTo(new Span<Vector4>(pointMetaDestination, LocalLightShadowLimits.MaxShadowedPointLights));
            spotMeta.CopyTo(new Span<Vector4>(spotMetaDestination, LocalLightShadowLimits.MaxShadowedSpotLights));
            pointMatrices.CopyTo(new Span<Matrix4x4>(pointMatrixDestination, LocalLightShadowLimits.MaxPointShadowFaces));
            pointRects.CopyTo(new Span<Vector4>(pointRectDestination, LocalLightShadowLimits.MaxPointShadowFaces));
            spotMatrices.CopyTo(new Span<Matrix4x4>(spotMatrixDestination, LocalLightShadowLimits.MaxShadowedSpotLights));
            spotRects.CopyTo(new Span<Vector4>(spotRectDestination, LocalLightShadowLimits.MaxShadowedSpotLights));
        }
    }

    public static void SetSpotLights(
        ref PmxFrameUniformData data,
        IReadOnlyList<SpotLightData>? lights,
        Matrix4x4 view)
    {
        Span<Vector4> positionRanges = stackalloc Vector4[SpotLightPacking.MaxLights];
        Span<Vector4> directionOuterCosines = stackalloc Vector4[SpotLightPacking.MaxLights];
        Span<Vector4> colorIntensities = stackalloc Vector4[SpotLightPacking.MaxLights];
        Span<Vector4> coneParameters = stackalloc Vector4[SpotLightPacking.MaxLights];
        int count = SpotLightPacking.PackViewSpace(
            lights, view, positionRanges, directionOuterCosines, colorIntensities, coneParameters);
        data.SpotLightMeta = new Vector4(count, 0.0f, 0.0f, 0.0f);

        fixed (float* destinationPositions = data.SpotLightPositionRanges)
        fixed (float* destinationDirections = data.SpotLightDirectionOuterCosines)
        fixed (float* destinationColors = data.SpotLightColorIntensities)
        fixed (float* destinationCones = data.SpotLightConeParameters)
        {
            positionRanges.CopyTo(new Span<Vector4>(destinationPositions, SpotLightPacking.MaxLights));
            directionOuterCosines.CopyTo(new Span<Vector4>(destinationDirections, SpotLightPacking.MaxLights));
            colorIntensities.CopyTo(new Span<Vector4>(destinationColors, SpotLightPacking.MaxLights));
            coneParameters.CopyTo(new Span<Vector4>(destinationCones, SpotLightPacking.MaxLights));
        }
    }

    public static void SetPointLights(
        ref PmxFrameUniformData data,
        IReadOnlyList<PointLightData>? lights,
        Matrix4x4 view)
    {
        Span<Vector4> positionRanges = stackalloc Vector4[PointLightPacking.MaxLights];
        Span<Vector4> colorIntensities = stackalloc Vector4[PointLightPacking.MaxLights];
        int count = PointLightPacking.PackViewSpace(lights, view, positionRanges, colorIntensities);
        data.PointLightMeta = new Vector4(count, 0.0f, 0.0f, 0.0f);

        fixed (float* destinationPositions = data.PointLightPositionRanges)
        fixed (float* destinationColors = data.PointLightColorIntensities)
        {
            positionRanges.CopyTo(new Span<Vector4>(destinationPositions, PointLightPacking.MaxLights));
            colorIntensities.CopyTo(new Span<Vector4>(destinationColors, PointLightPacking.MaxLights));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PmxMaterialUniformData
    {
        public Vector4 Ambient;
        public Vector4 Diffuse;
        public Vector4 Specular;
        public Vector4 TextureMultiply;
        public Vector4 TextureAdd;
        public Vector4 SphereMultiply;
        public Vector4 SphereAdd;
        public Vector4 ToonMultiply;
        public Vector4 ToonAdd;
        public Vector4 Modes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PmxEdgeUniformData
    {
        public Matrix4x4 WorldView;
        public Matrix4x4 WorldViewProjection;
        public Vector4 ScreenAndEdgeSize;
        public Vector4 EdgeColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PmxGroundShadowUniformData
    {
        public Matrix4x4 WorldViewProjection;
        public Vector4 ShadowColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PmxShadowDepthUniformData
    {
        public Matrix4x4 WorldLightViewProjection;
        public Vector4 Parameters;
    }
}
