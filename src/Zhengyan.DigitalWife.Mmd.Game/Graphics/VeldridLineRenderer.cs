using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.SPIRV;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public sealed class VeldridLineRenderer : ILineRenderer
{
    private readonly VulkanRenderer _renderer;
    private DeviceBuffer[] _vertices;
    private uint _capacity;
    private readonly DeviceBuffer[] _uniforms;
    private readonly ResourceLayout _layout;
    private readonly Shader[] _shaders;
    private readonly ShaderSetDescription _shaderSet;
    private readonly ResourceSet[] _sets;
    private readonly List<(OutputDescription Output, bool Depth, Pipeline Pipeline)> _pipelines = [];

    public VeldridLineRenderer(VulkanRenderer renderer, uint initialCapacityBytes = 4096)
    {
        _renderer = renderer;
        ResourceFactory factory = renderer.ResourceFactory;
        _capacity = Math.Max(initialCapacityBytes, 256);
        _vertices = Enumerable.Range(0, VulkanRenderer.FrameSlotCount).Select(_ => factory.CreateBuffer(new BufferDescription(_capacity, BufferUsage.VertexBuffer | BufferUsage.Dynamic))).ToArray();
        _uniforms = Enumerable.Range(0, VulkanRenderer.FrameSlotCount).Select(_ => factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<Matrix4x4>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic))).ToArray();
        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("LineFrame", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
        _sets = _uniforms.Select(buffer => factory.CreateResourceSet(new ResourceSetDescription(_layout, buffer))).ToArray();
        _shaders = factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("line.vert", VertexSource, ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("line.frag", FragmentSource, ShaderStages.Fragment));
        _shaderSet = new ShaderSetDescription(
            [new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
                new VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float3))],
            _shaders);
    }

    public void Draw(ReadOnlySpan<float> interleavedPositionColor, int vertexCount, Matrix4x4 worldViewProjection, bool depthTest = false)
    {
        if (!_renderer.IsFrameOpen || vertexCount <= 0) return;
        uint byteCount = checked((uint)(vertexCount * 6 * sizeof(float)));
        EnsureCapacity(byteCount);
        CommandList commands = _renderer.CommandList;
        int slot = _renderer.CurrentFrameSlot;
        commands.UpdateBuffer(_vertices[slot], 0, interleavedPositionColor[..(vertexCount * 6)]);
        commands.UpdateBuffer(_uniforms[slot], 0, worldViewProjection);
        commands.SetPipeline(GetPipeline(_renderer.CurrentOutputDescription, depthTest));
        commands.SetVertexBuffer(0, _vertices[slot]);
        commands.SetGraphicsResourceSet(0, _sets[slot]);
        commands.Draw((uint)vertexCount);
    }

    public void Dispose()
    {
        foreach ((_, _, Pipeline pipeline) in _pipelines) pipeline.Dispose();
        foreach (Shader shader in _shaders) shader.Dispose();
        foreach (ResourceSet set in _sets) set.Dispose();
        _layout.Dispose();
        foreach (DeviceBuffer buffer in _uniforms) buffer.Dispose();
        foreach (DeviceBuffer buffer in _vertices) buffer.Dispose();
    }

    private void EnsureCapacity(uint bytes)
    {
        if (bytes <= _capacity) return;
        _capacity = Math.Max(bytes, _capacity * 2);
        foreach (DeviceBuffer buffer in _vertices) buffer.Dispose();
        _vertices = Enumerable.Range(0, VulkanRenderer.FrameSlotCount).Select(_ => _renderer.ResourceFactory.CreateBuffer(new BufferDescription(_capacity, BufferUsage.VertexBuffer | BufferUsage.Dynamic))).ToArray();
    }

    private Pipeline GetPipeline(OutputDescription output, bool depth)
    {
        foreach ((OutputDescription candidate, bool candidateDepth, Pipeline pipeline) in _pipelines)
            if (candidate.Equals(output) && candidateDepth == depth) return pipeline;
        Pipeline created = _renderer.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            depth ? new DepthStencilStateDescription(true, false, ComparisonKind.LessEqual) : DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.LineList,
            _shaderSet,
            [_layout], output));
        _pipelines.Add((output, depth, created));
        return created;
    }

    private const string VertexSource = """
        layout(set=0,binding=0,std140) uniform LineFrame { mat4 wvp; } frame;
        layout(location=0) in vec3 in_Pos;
        layout(location=1) in vec3 in_Color;
        layout(location=0) out vec3 vs_Color;
        void main(){ gl_Position=frame.wvp*vec4(in_Pos,1.0); vs_Color=in_Color; }
        """;
    private const string FragmentSource = """
        layout(location=0) in vec3 vs_Color;
        layout(location=0) out vec4 out_Color;
        void main(){ out_Color=vec4(vs_Color,1.0); }
        """;
}
