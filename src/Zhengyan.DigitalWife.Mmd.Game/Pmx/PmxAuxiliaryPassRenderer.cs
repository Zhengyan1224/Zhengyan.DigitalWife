using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Silk.NET.OpenGLES;
using Veldrid;
using Veldrid.SPIRV;
using EngineGraphicsDevice = Zhengyan.DigitalWife.Mmd.Game.Graphics.GraphicsDevice;
using VeldridShader = Veldrid.Shader;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

internal interface IPmxAuxiliaryPassRenderer : IDisposable
{
    int DrawEdge(
        PmxGpuResources resources,
        IReadOnlyList<Zhengyan.DigitalWife.Mmd.MMDMesh> meshes,
        Matrix4x4 world,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector2 screenSize);

    int DrawGroundShadow(
        PmxGpuResources resources,
        IReadOnlyList<Zhengyan.DigitalWife.Mmd.MMDMesh> meshes,
        Matrix4x4 worldViewProjection,
        Vector4 shadowColor);

    int DrawShadowDepth(
        PmxGpuResources resources,
        IReadOnlyList<Zhengyan.DigitalWife.Mmd.MMDMesh> meshes,
        Matrix4x4 worldLightViewProjection);
}

internal static class PmxAuxiliaryPassRendererFactory
{
    public static IPmxAuxiliaryPassRenderer Create(EngineGraphicsDevice graphicsDevice, PmxGpuResources resources)
    {
        return graphicsDevice.Renderer switch
        {
            OpenGlRenderer openGl => new OpenGlPmxAuxiliaryPassRenderer(openGl.Gl, resources),
            VulkanRenderer vulkan => new VeldridPmxAuxiliaryPassRenderer(vulkan, resources),
            _ => throw new NotSupportedException($"PMX auxiliary passes are not implemented for {graphicsDevice.Backend}.")
        };
    }
}

internal sealed unsafe class OpenGlPmxAuxiliaryPassRenderer : IPmxAuxiliaryPassRenderer
{
    private readonly GL _gl;
    private readonly PmxEdgeShader _edgeShader;
    private readonly PmxGroundShadowShader _groundShadowShader;
    private readonly PmxShadowDepthShader _shadowDepthShader;
    private readonly uint _edgeVao;
    private readonly uint _groundShadowVao;
    private readonly uint _shadowDepthVao;
    private bool _disposed;

    public OpenGlPmxAuxiliaryPassRenderer(GL gl, PmxGpuResources resources)
    {
        _gl = gl;
        _edgeShader = new PmxEdgeShader(gl);
        _groundShadowShader = new PmxGroundShadowShader(gl);
        _shadowDepthShader = new PmxShadowDepthShader(gl);

        _edgeVao = gl.GenVertexArray();
        gl.BindVertexArray(_edgeVao);
        gl.BindBuffer(GLEnum.ArrayBuffer, resources.PositionBuffer.LegacyBufferId);
        gl.VertexAttribPointer(_edgeShader.InPos, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);
        gl.EnableVertexAttribArray(_edgeShader.InPos);
        gl.BindBuffer(GLEnum.ArrayBuffer, resources.NormalBuffer.LegacyBufferId);
        gl.VertexAttribPointer(_edgeShader.InNor, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);
        gl.EnableVertexAttribArray(_edgeShader.InNor);
        gl.BindBuffer(GLEnum.ElementArrayBuffer, resources.IndexBuffer.LegacyBufferId);

        _groundShadowVao = gl.GenVertexArray();
        gl.BindVertexArray(_groundShadowVao);
        gl.BindBuffer(GLEnum.ArrayBuffer, resources.PositionBuffer.LegacyBufferId);
        gl.VertexAttribPointer(_groundShadowShader.InPos, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);
        gl.EnableVertexAttribArray(_groundShadowShader.InPos);
        gl.BindBuffer(GLEnum.ElementArrayBuffer, resources.IndexBuffer.LegacyBufferId);

        _shadowDepthVao = gl.GenVertexArray();
        gl.BindVertexArray(_shadowDepthVao);
        gl.BindBuffer(GLEnum.ArrayBuffer, resources.PositionBuffer.LegacyBufferId);
        gl.VertexAttribPointer(_shadowDepthShader.InPos, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);
        gl.EnableVertexAttribArray(_shadowDepthShader.InPos);
        gl.BindBuffer(GLEnum.ElementArrayBuffer, resources.IndexBuffer.LegacyBufferId);
        gl.BindVertexArray(0);
    }

    public int DrawEdge(
        PmxGpuResources resources,
        IReadOnlyList<Zhengyan.DigitalWife.Mmd.MMDMesh> meshes,
        Matrix4x4 world,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector2 screenSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Matrix4x4 worldView = world * view;
        Matrix4x4 worldViewProjection = worldView * projection;
        _gl.Enable(GLEnum.DepthTest);
        _gl.Enable(GLEnum.Blend);
        _gl.Enable(GLEnum.CullFace);
        _gl.CullFace(GLEnum.Front);
        _gl.UseProgram(_edgeShader.Id);
        _gl.BindVertexArray(_edgeVao);
        _gl.SetUniform(_edgeShader.UniWVP, worldViewProjection);
        _gl.SetUniform(_edgeShader.UniWV, worldView);
        _gl.SetUniform(_edgeShader.UniScreenSize, screenSize);

        int count = 0;
        foreach (Zhengyan.DigitalWife.Mmd.MMDMesh mesh in meshes)
        {
            Zhengyan.DigitalWife.Mmd.MMDMaterial material = mesh.Material;
            if (material.EdgeFlag == 0 || material.Alpha <= 0.0f) continue;

            PmxGpuResources.PmxEdgeUniformData data = new()
            {
                WorldView = worldView,
                WorldViewProjection = worldViewProjection,
                ScreenAndEdgeSize = new Vector4(Math.Max(screenSize.X, 1.0f), Math.Max(screenSize.Y, 1.0f), material.EdgeSize, 0.0f),
                EdgeColor = material.EdgeColor
            };
            resources.EdgeUniformBuffer.Update(new ReadOnlySpan<PmxGpuResources.PmxEdgeUniformData>(in data));
            _gl.SetUniform(_edgeShader.UniEdgeSize, material.EdgeSize);
            _gl.SetUniform(_edgeShader.UniEdgeColor, material.EdgeColor);
            _gl.DrawElements(GLEnum.Triangles, mesh.VertexCount, GLEnum.UnsignedInt, (void*)(mesh.BeginIndex * sizeof(uint)));
            count++;
        }

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        return count;
    }

    public int DrawGroundShadow(
        PmxGpuResources resources,
        IReadOnlyList<Zhengyan.DigitalWife.Mmd.MMDMesh> meshes,
        Matrix4x4 worldViewProjection,
        Vector4 shadowColor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PmxGpuResources.PmxGroundShadowUniformData data = new()
        {
            WorldViewProjection = worldViewProjection,
            ShadowColor = shadowColor
        };
        resources.GroundShadowUniformBuffer.Update(new ReadOnlySpan<PmxGpuResources.PmxGroundShadowUniformData>(in data));

        _gl.Enable(GLEnum.DepthTest);
        _gl.Enable(GLEnum.PolygonOffsetFill);
        _gl.PolygonOffset(-1.0f, -1.0f);
        _gl.DepthMask(false);
        if (shadowColor.W < 1.0f)
        {
            _gl.Enable(GLEnum.Blend);
            _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
            _gl.Enable(GLEnum.StencilTest);
            _gl.StencilFuncSeparate(GLEnum.FrontAndBack, GLEnum.Notequal, 1, 1);
            _gl.StencilOp(GLEnum.Keep, GLEnum.Keep, GLEnum.Replace);
        }
        else
        {
            _gl.Disable(GLEnum.Blend);
        }

        _gl.Disable(GLEnum.CullFace);
        _gl.UseProgram(_groundShadowShader.Id);
        _gl.BindVertexArray(_groundShadowVao);
        _gl.SetUniform(_groundShadowShader.UniWVP, worldViewProjection);
        _gl.SetUniform(_groundShadowShader.UniShadowColor, shadowColor);

        int count = 0;
        foreach (Zhengyan.DigitalWife.Mmd.MMDMesh mesh in meshes)
        {
            if (!mesh.Material.GroundShadow || mesh.Material.Alpha <= 0.0f) continue;
            _gl.DrawElements(GLEnum.Triangles, mesh.VertexCount, GLEnum.UnsignedInt, (void*)(mesh.BeginIndex * sizeof(uint)));
            count++;
        }

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _gl.DepthMask(true);
        _gl.Disable(GLEnum.StencilTest);
        _gl.Disable(GLEnum.PolygonOffsetFill);
        return count;
    }

    public int DrawShadowDepth(
        PmxGpuResources resources,
        IReadOnlyList<Zhengyan.DigitalWife.Mmd.MMDMesh> meshes,
        Matrix4x4 worldLightViewProjection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PmxGpuResources.PmxShadowDepthUniformData data = new() { WorldLightViewProjection = worldLightViewProjection };
        resources.ShadowDepthUniformBuffer.Update(new ReadOnlySpan<PmxGpuResources.PmxShadowDepthUniformData>(in data));
        _gl.Enable(GLEnum.DepthTest);
        _gl.DepthMask(true);
        _gl.Disable(GLEnum.Blend);
        _gl.UseProgram(_shadowDepthShader.Id);
        _gl.BindVertexArray(_shadowDepthVao);
        _gl.SetUniform(_shadowDepthShader.UniWorldLightViewProjection, worldLightViewProjection);

        int count = 0;
        foreach (Zhengyan.DigitalWife.Mmd.MMDMesh mesh in meshes)
        {
            Zhengyan.DigitalWife.Mmd.MMDMaterial material = mesh.Material;
            if (!material.ShadowCaster || material.Alpha <= 0.01f) continue;
            if (material.BothFace) _gl.Disable(GLEnum.CullFace);
            else
            {
                _gl.Enable(GLEnum.CullFace);
                _gl.CullFace(GLEnum.Back);
            }

            _gl.DrawElements(GLEnum.Triangles, mesh.VertexCount, GLEnum.UnsignedInt, (void*)(mesh.BeginIndex * sizeof(uint)));
            count++;
        }

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _gl.Disable(GLEnum.CullFace);
        return count;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gl.DeleteVertexArray(_edgeVao);
        _gl.DeleteVertexArray(_groundShadowVao);
        _gl.DeleteVertexArray(_shadowDepthVao);
        _edgeShader.Dispose();
        _groundShadowShader.Dispose();
        _shadowDepthShader.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed class VeldridPmxAuxiliaryPassRenderer : IPmxAuxiliaryPassRenderer
{
    private readonly VulkanRenderer _renderer;
    private readonly ResourceLayout _edgeLayout;
    private readonly ResourceLayout _groundLayout;
    private readonly ResourceLayout _depthLayout;
    private readonly ResourceSet _edgeSet;
    private readonly ResourceSet _groundSet;
    private readonly ResourceSet _depthSet;
    private readonly VeldridShader[] _edgeShaders;
    private readonly VeldridShader[] _groundShaders;
    private readonly VeldridShader[] _depthShaders;
    private readonly ShaderSetDescription _edgeShaderSet;
    private readonly ShaderSetDescription _groundShaderSet;
    private readonly ShaderSetDescription _depthShaderSet;
    private readonly List<EdgePipelineBundle> _edgePipelines = [];
    private readonly List<GroundPipelineBundle> _groundPipelines = [];
    private readonly List<DepthPipelineBundle> _depthPipelines = [];
    private bool _disposed;

    public VeldridPmxAuxiliaryPassRenderer(VulkanRenderer renderer, PmxGpuResources resources)
    {
        _renderer = renderer;
        ResourceFactory factory = renderer.ResourceFactory;
        _edgeLayout = CreateUniformLayout(factory, "PmxEdge", ShaderStages.Vertex | ShaderStages.Fragment);
        _groundLayout = CreateUniformLayout(factory, "PmxGroundShadow", ShaderStages.Vertex | ShaderStages.Fragment);
        _depthLayout = CreateUniformLayout(factory, "PmxShadowDepth", ShaderStages.Vertex);
        _edgeSet = factory.CreateResourceSet(new ResourceSetDescription(_edgeLayout, RequireDeviceBuffer(resources.EdgeUniformBuffer)));
        _groundSet = factory.CreateResourceSet(new ResourceSetDescription(_groundLayout, RequireDeviceBuffer(resources.GroundShadowUniformBuffer)));
        _depthSet = factory.CreateResourceSet(new ResourceSetDescription(_depthLayout, RequireDeviceBuffer(resources.ShadowDepthUniformBuffer)));

        _edgeShaders = CreateShaders(factory, "pmx_edge", EdgeVertexShaderSource, EdgeFragmentShaderSource);
        _groundShaders = CreateShaders(factory, "pmx_ground_shadow", GroundVertexShaderSource, GroundFragmentShaderSource);
        _depthShaders = CreateShaders(factory, "pmx_shadow_depth", DepthVertexShaderSource, DepthFragmentShaderSource);
        _edgeShaderSet = new ShaderSetDescription(
            [
                new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3)),
                new VertexLayoutDescription(new VertexElementDescription("Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3))
            ],
            _edgeShaders);
        _groundShaderSet = new ShaderSetDescription(
            [new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3))],
            _groundShaders);
        _depthShaderSet = new ShaderSetDescription(
            [new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3))],
            _depthShaders);
    }

    public int DrawEdge(
        PmxGpuResources resources,
        IReadOnlyList<Zhengyan.DigitalWife.Mmd.MMDMesh> meshes,
        Matrix4x4 world,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector2 screenSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_renderer.IsFrameOpen) return 0;
        Matrix4x4 worldView = world * view;
        Matrix4x4 worldViewProjection = worldView * projection;
        CommandList commands = _renderer.CommandList;
        commands.SetPipeline(GetEdgePipeline(_renderer.CurrentOutputDescription));
        commands.SetVertexBuffer(0, RequireDeviceBuffer(resources.PositionBuffer));
        commands.SetVertexBuffer(1, RequireDeviceBuffer(resources.NormalBuffer));
        commands.SetIndexBuffer(RequireDeviceBuffer(resources.IndexBuffer), IndexFormat.UInt32);
        commands.SetGraphicsResourceSet(0, _edgeSet);

        int count = 0;
        foreach (Zhengyan.DigitalWife.Mmd.MMDMesh mesh in meshes)
        {
            Zhengyan.DigitalWife.Mmd.MMDMaterial material = mesh.Material;
            if (material.EdgeFlag == 0 || material.Alpha <= 0.0f) continue;
            PmxGpuResources.PmxEdgeUniformData data = new()
            {
                WorldView = worldView,
                WorldViewProjection = worldViewProjection,
                ScreenAndEdgeSize = new Vector4(Math.Max(screenSize.X, 1.0f), Math.Max(screenSize.Y, 1.0f), material.EdgeSize, 0.0f),
                EdgeColor = material.EdgeColor
            };
            commands.UpdateBuffer(RequireDeviceBuffer(resources.EdgeUniformBuffer), 0, data);
            commands.DrawIndexed((uint)mesh.VertexCount, 1, (uint)mesh.BeginIndex, 0, 0);
            count++;
        }

        return count;
    }

    public int DrawGroundShadow(
        PmxGpuResources resources,
        IReadOnlyList<Zhengyan.DigitalWife.Mmd.MMDMesh> meshes,
        Matrix4x4 worldViewProjection,
        Vector4 shadowColor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_renderer.IsFrameOpen) return 0;
        CommandList commands = _renderer.CommandList;
        GroundPipelineBundle pipelines = GetGroundPipelines(_renderer.CurrentOutputDescription);
        commands.SetPipeline(shadowColor.W < 1.0f ? pipelines.Alpha : pipelines.Opaque);
        commands.SetVertexBuffer(0, RequireDeviceBuffer(resources.PositionBuffer));
        commands.SetIndexBuffer(RequireDeviceBuffer(resources.IndexBuffer), IndexFormat.UInt32);
        commands.SetGraphicsResourceSet(0, _groundSet);
        PmxGpuResources.PmxGroundShadowUniformData data = new()
        {
            WorldViewProjection = worldViewProjection,
            ShadowColor = shadowColor
        };
        commands.UpdateBuffer(RequireDeviceBuffer(resources.GroundShadowUniformBuffer), 0, data);

        int count = 0;
        foreach (Zhengyan.DigitalWife.Mmd.MMDMesh mesh in meshes)
        {
            if (!mesh.Material.GroundShadow || mesh.Material.Alpha <= 0.0f) continue;
            commands.DrawIndexed((uint)mesh.VertexCount, 1, (uint)mesh.BeginIndex, 0, 0);
            count++;
        }

        return count;
    }

    public int DrawShadowDepth(
        PmxGpuResources resources,
        IReadOnlyList<Zhengyan.DigitalWife.Mmd.MMDMesh> meshes,
        Matrix4x4 worldLightViewProjection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_renderer.IsFrameOpen) return 0;
        CommandList commands = _renderer.CommandList;
        DepthPipelineBundle pipelines = GetDepthPipelines(_renderer.CurrentOutputDescription);
        commands.SetPipeline(pipelines.Culled);
        commands.SetVertexBuffer(0, RequireDeviceBuffer(resources.PositionBuffer));
        commands.SetIndexBuffer(RequireDeviceBuffer(resources.IndexBuffer), IndexFormat.UInt32);
        PmxGpuResources.PmxShadowDepthUniformData data = new() { WorldLightViewProjection = worldLightViewProjection };
        commands.UpdateBuffer(RequireDeviceBuffer(resources.ShadowDepthUniformBuffer), 0, data);

        int count = 0;
        foreach (Zhengyan.DigitalWife.Mmd.MMDMesh mesh in meshes)
        {
            Zhengyan.DigitalWife.Mmd.MMDMaterial material = mesh.Material;
            if (!material.ShadowCaster || material.Alpha <= 0.01f) continue;
            commands.SetPipeline(material.BothFace ? pipelines.DoubleSided : pipelines.Culled);
            commands.SetGraphicsResourceSet(0, _depthSet);
            commands.DrawIndexed((uint)mesh.VertexCount, 1, (uint)mesh.BeginIndex, 0, 0);
            count++;
        }

        return count;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (EdgePipelineBundle bundle in _edgePipelines) bundle.Pipeline.Dispose();
        foreach (GroundPipelineBundle bundle in _groundPipelines)
        {
            bundle.Alpha.Dispose();
            bundle.Opaque.Dispose();
        }
        foreach (DepthPipelineBundle bundle in _depthPipelines)
        {
            bundle.Culled.Dispose();
            bundle.DoubleSided.Dispose();
        }
        _edgeSet.Dispose();
        _groundSet.Dispose();
        _depthSet.Dispose();
        _edgeLayout.Dispose();
        _groundLayout.Dispose();
        _depthLayout.Dispose();
        DisposeShaders(_edgeShaders);
        DisposeShaders(_groundShaders);
        DisposeShaders(_depthShaders);
        GC.SuppressFinalize(this);
    }

    private Pipeline GetEdgePipeline(OutputDescription output)
    {
        EdgePipelineBundle? existing = _edgePipelines.FirstOrDefault(bundle => bundle.Output.Equals(output));
        if (existing is not null) return existing.Pipeline;
        Pipeline pipeline = _renderer.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
            new RasterizerStateDescription(FaceCullMode.Front, PolygonFillMode.Solid, _renderer.RasterizerFrontFace, true, false),
            PrimitiveTopology.TriangleList,
            _edgeShaderSet,
            [_edgeLayout],
            output));
        _edgePipelines.Add(new EdgePipelineBundle(output, pipeline));
        return pipeline;
    }

    private GroundPipelineBundle GetGroundPipelines(OutputDescription output)
    {
        GroundPipelineBundle? existing = _groundPipelines.FirstOrDefault(bundle => bundle.Output.Equals(output));
        if (existing is not null) return existing;
        StencilBehaviorDescription stencil = new(
            StencilOperation.Keep,
            StencilOperation.Replace,
            StencilOperation.Keep,
            ComparisonKind.NotEqual);
        DepthStencilStateDescription alphaDepth = new(
            true, false, ComparisonKind.LessEqual, true, stencil, stencil, 0xFF, 0xFF, 1);
        Pipeline alpha = CreateGroundPipeline(output, BlendStateDescription.SingleAlphaBlend, alphaDepth);
        Pipeline opaque = CreateGroundPipeline(
            output,
            BlendStateDescription.SingleDisabled,
            new DepthStencilStateDescription(true, false, ComparisonKind.LessEqual));
        GroundPipelineBundle created = new(output, alpha, opaque);
        _groundPipelines.Add(created);
        return created;
    }

    private Pipeline CreateGroundPipeline(
        OutputDescription output,
        BlendStateDescription blend,
        DepthStencilStateDescription depthStencil)
    {
        return _renderer.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            blend,
            depthStencil,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            _groundShaderSet,
            [_groundLayout],
            output));
    }

    private DepthPipelineBundle GetDepthPipelines(OutputDescription output)
    {
        DepthPipelineBundle? existing = _depthPipelines.FirstOrDefault(bundle => bundle.Output.Equals(output));
        if (existing is not null) return existing;
        BlendStateDescription blend = output.ColorAttachments.Length == 0
            ? BlendStateDescription.Empty
            : BlendStateDescription.SingleDisabled;
        Pipeline culled = CreateDepthPipeline(output, blend, FaceCullMode.Back);
        Pipeline doubleSided = CreateDepthPipeline(output, blend, FaceCullMode.None);
        DepthPipelineBundle created = new(output, culled, doubleSided);
        _depthPipelines.Add(created);
        return created;
    }

    private Pipeline CreateDepthPipeline(OutputDescription output, BlendStateDescription blend, FaceCullMode cullMode)
    {
        return _renderer.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            blend,
            new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
            new RasterizerStateDescription(cullMode, PolygonFillMode.Solid, _renderer.RasterizerFrontFace, true, false),
            PrimitiveTopology.TriangleList,
            _depthShaderSet,
            [_depthLayout],
            output));
    }

    private static ResourceLayout CreateUniformLayout(ResourceFactory factory, string name, ShaderStages stages)
    {
        return factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription(name, ResourceKind.UniformBuffer, stages)));
    }

    private static VeldridShader[] CreateShaders(
        ResourceFactory factory,
        string name,
        string vertexSource,
        string fragmentSource)
    {
        return factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource($"{name}.vert", vertexSource, ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource($"{name}.frag", fragmentSource, ShaderStages.Fragment));
    }

    private static DeviceBuffer RequireDeviceBuffer(IGpuBuffer buffer)
    {
        return buffer.NativeResource as DeviceBuffer
            ?? throw new InvalidOperationException("PMX Vulkan auxiliary pass requires a Veldrid device buffer.");
    }

    private static void DisposeShaders(IEnumerable<VeldridShader> shaders)
    {
        foreach (VeldridShader shader in shaders) shader.Dispose();
    }

    private sealed record EdgePipelineBundle(OutputDescription Output, Pipeline Pipeline);
    private sealed record GroundPipelineBundle(OutputDescription Output, Pipeline Alpha, Pipeline Opaque);
    private sealed record DepthPipelineBundle(OutputDescription Output, Pipeline Culled, Pipeline DoubleSided);

    private const string EdgeVertexShaderSource = """
        layout(set = 0, binding = 0, std140) uniform PmxEdge
        {
            mat4 u_WV;
            mat4 u_WVP;
            vec4 u_ScreenAndEdgeSize;
            vec4 u_EdgeColor;
        } u_Edge;
        layout(location = 0) in vec3 in_Pos;
        layout(location = 1) in vec3 in_Nor;
        void main()
        {
            vec3 normal = mat3(u_Edge.u_WV) * in_Nor;
            vec4 position = u_Edge.u_WVP * vec4(in_Pos, 1.0);
            vec2 screenNormal = normalize(vec2(normal));
            position.xy += screenNormal / (u_Edge.u_ScreenAndEdgeSize.xy * 0.5)
                * u_Edge.u_ScreenAndEdgeSize.z * position.w;
            gl_Position = position;
        }
        """;

    private const string EdgeFragmentShaderSource = """
        layout(set = 0, binding = 0, std140) uniform PmxEdge
        {
            mat4 u_WV;
            mat4 u_WVP;
            vec4 u_ScreenAndEdgeSize;
            vec4 u_EdgeColor;
        } u_Edge;
        layout(location = 0) out vec4 out_Color;
        void main() { out_Color = u_Edge.u_EdgeColor; }
        """;

    private const string GroundVertexShaderSource = """
        layout(set = 0, binding = 0, std140) uniform PmxGroundShadow
        {
            mat4 u_WVP;
            vec4 u_ShadowColor;
        } u_Ground;
        layout(location = 0) in vec3 in_Pos;
        void main()
        {
            vec4 position = u_Ground.u_WVP * vec4(in_Pos, 1.0);
            position.z -= 0.0001 * position.w;
            gl_Position = position;
        }
        """;

    private const string GroundFragmentShaderSource = """
        layout(set = 0, binding = 0, std140) uniform PmxGroundShadow
        {
            mat4 u_WVP;
            vec4 u_ShadowColor;
        } u_Ground;
        layout(location = 0) out vec4 out_Color;
        void main() { out_Color = u_Ground.u_ShadowColor; }
        """;

    private const string DepthVertexShaderSource = """
        layout(set = 0, binding = 0, std140) uniform PmxShadowDepth
        {
            mat4 u_WorldLightViewProjection;
        } u_Depth;
        layout(location = 0) in vec3 in_Pos;
        void main() { gl_Position = u_Depth.u_WorldLightViewProjection * vec4(in_Pos, 1.0); }
        """;

    private const string DepthFragmentShaderSource = """
        void main() { }
        """;
}
