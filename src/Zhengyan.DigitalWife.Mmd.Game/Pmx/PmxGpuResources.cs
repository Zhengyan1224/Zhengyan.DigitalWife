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
}
