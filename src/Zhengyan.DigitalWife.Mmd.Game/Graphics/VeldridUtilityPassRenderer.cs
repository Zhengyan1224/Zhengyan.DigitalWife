using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.SPIRV;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>Small backend utility passes for operations without a portable command-list primitive.</summary>
internal sealed class VeldridUtilityPassRenderer : IDisposable
{
    private readonly VulkanRenderer _renderer;
    private readonly DeviceBuffer _uniformBuffer;
    private readonly ResourceLayout _layout;
    private readonly ResourceSet _resourceSet;
    private readonly Shader[] _shaders;
    private readonly ShaderSetDescription _shaderSet;
    private readonly List<PipelineBundle> _pipelines = [];
    private bool _disposed;

    public VeldridUtilityPassRenderer(VulkanRenderer renderer)
    {
        _renderer = renderer;
        ResourceFactory factory = renderer.ResourceFactory;
        _uniformBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)Marshal.SizeOf<UtilityUniforms>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("UtilityFrame", ResourceKind.UniformBuffer, ShaderStages.Fragment)));
        _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout, _uniformBuffer));
        _shaders = factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("utility.vert", VertexSource, ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("utility.frag", FragmentSource, ShaderStages.Fragment));
        _shaderSet = new ShaderSetDescription([], _shaders);
    }

    public void ClearViewport(int x, int y, int width, int height, Vector4 color)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_renderer.IsFrameOpen) return;

        CommandList commands = _renderer.CommandList;
        commands.UpdateBuffer(_uniformBuffer, 0, new UtilityUniforms { Color = color });
        commands.SetViewport(0, new Viewport(x, y, Math.Max(width, 1), Math.Max(height, 1), 0, 1));
        commands.SetScissorRect(0, (uint)Math.Max(x, 0), (uint)Math.Max(y, 0),
            (uint)Math.Max(width, 1), (uint)Math.Max(height, 1));
        commands.SetPipeline(GetPipeline(_renderer.CurrentOutputDescription, UtilityPassKind.Clear));
        commands.SetGraphicsResourceSet(0, _resourceSet);
        commands.Draw(3);
    }

    public void ForceOpaqueAlpha(VeldridRenderTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_renderer.IsFrameOpen) return;

        target.ResumePass();
        CommandList commands = _renderer.CommandList;
        commands.UpdateBuffer(_uniformBuffer, 0, new UtilityUniforms { Color = Vector4.One });
        commands.SetFullViewports();
        commands.SetFullScissorRects();
        commands.SetPipeline(GetPipeline(_renderer.CurrentOutputDescription, UtilityPassKind.OpaqueAlpha));
        commands.SetGraphicsResourceSet(0, _resourceSet);
        commands.Draw(3);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (PipelineBundle bundle in _pipelines) bundle.Pipeline.Dispose();
        foreach (Shader shader in _shaders) shader.Dispose();
        _resourceSet.Dispose();
        _layout.Dispose();
        _uniformBuffer.Dispose();
    }

    private Pipeline GetPipeline(OutputDescription output, UtilityPassKind kind)
    {
        PipelineBundle? existing = _pipelines.FirstOrDefault(item => item.Output.Equals(output) && item.Kind == kind);
        if (existing is not null) return existing.Pipeline;

        BlendStateDescription blend;
        DepthStencilStateDescription depthStencil;
        if (kind == UtilityPassKind.Clear)
        {
            blend = BlendStateDescription.SingleOverrideBlend;
            StencilBehaviorDescription stencil = new(
                StencilOperation.Replace,
                StencilOperation.Replace,
                StencilOperation.Replace,
                ComparisonKind.Always);
            depthStencil = new DepthStencilStateDescription(
                true, true, ComparisonKind.Always, true, stencil, stencil, 0xFF, 0xFF, 0);
        }
        else
        {
            BlendAttachmentDescription alphaOnly = new(
                false,
                ColorWriteMask.Alpha,
                BlendFactor.One,
                BlendFactor.Zero,
                BlendFunction.Add,
                BlendFactor.One,
                BlendFactor.Zero,
                BlendFunction.Add);
            blend = new BlendStateDescription(RgbaFloat.Black, alphaOnly);
            depthStencil = DepthStencilStateDescription.Disabled;
        }

        Pipeline pipeline = _renderer.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            blend,
            depthStencil,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            _shaderSet,
            [_layout],
            output));
        _pipelines.Add(new PipelineBundle(output, kind, pipeline));
        return pipeline;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UtilityUniforms
    {
        public Vector4 Color;
    }

    private enum UtilityPassKind
    {
        Clear,
        OpaqueAlpha
    }

    private sealed record PipelineBundle(OutputDescription Output, UtilityPassKind Kind, Pipeline Pipeline);

    private const string VertexSource = """
        void main()
        {
            vec2 position = gl_VertexIndex == 0 ? vec2(-1.0, -1.0)
                : gl_VertexIndex == 1 ? vec2(3.0, -1.0)
                : vec2(-1.0, 3.0);
            gl_Position = vec4(position, 0.0, 1.0);
        }
        """;

    private const string FragmentSource = """
        layout(set=0,binding=0,std140) uniform UtilityFrame { vec4 color; } frame;
        layout(location=0) out vec4 out_Color;
        void main()
        {
            gl_FragDepth = 1.0;
            out_Color = frame.color;
        }
        """;
}
