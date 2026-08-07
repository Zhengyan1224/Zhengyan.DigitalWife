using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.SPIRV;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>
/// Vulkan implementation of the screen-sprite pass. It consumes the same
/// <see cref="ScreenSpriteDrawCommand"/> values as the OpenGL compatibility pass.
/// </summary>
public sealed class VeldridScreenSpriteRenderer : IScreenSpriteRenderer
{
    private const int MaxVerticesPerSprite = 6;

    private readonly VulkanRenderer _renderer;
    private readonly Pipeline _pipeline;
    private readonly DeviceBuffer _vertexBuffer;
    private readonly DeviceBuffer _parametersBuffer;
    private readonly ResourceLayout _parametersLayout;
    private readonly ResourceLayout _textureLayout;
    private readonly ResourceSet _parametersSet;
    private readonly Sampler _sampler;
    private readonly Shader[] _shaders;
    private readonly Dictionary<TextureView, ResourceSet> _textureSets = [];
    private readonly float[] _vertices = new float[MaxVerticesPerSprite * 4];
    private bool _disposed;

    public VeldridScreenSpriteRenderer(VulkanRenderer renderer)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        ResourceFactory factory = renderer.ResourceFactory;

        _vertexBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)(_vertices.Length * sizeof(float)), BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _parametersBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)Marshal.SizeOf<SpriteParameters>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        _parametersLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SpriteParameters", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));
        _textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SpriteTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SpriteSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        _parametersSet = factory.CreateResourceSet(new ResourceSetDescription(_parametersLayout, _parametersBuffer));
        _sampler = factory.CreateSampler(SamplerDescription.Linear);

        ShaderDescription vertexDescription = VulkanShaderCompiler.CompileSource(
            "screen_sprite.vert", VertexShaderSource, ShaderStages.Vertex);
        ShaderDescription fragmentDescription = VulkanShaderCompiler.CompileSource(
            "screen_sprite.frag", FragmentShaderSource, ShaderStages.Fragment);
        _shaders = factory.CreateFromSpirv(vertexDescription, fragmentDescription);

        VertexLayoutDescription vertexLayout = new(
            new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float2),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));
        _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription([vertexLayout], _shaders),
            [_parametersLayout, _textureLayout],
            renderer.Device.SwapchainFramebuffer.OutputDescription));
    }

    public void Draw(IReadOnlyList<ScreenSpriteDrawCommand> commands, int targetWidth, int targetHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (commands.Count == 0 || !_renderer.IsFrameOpen)
        {
            return;
        }

        CommandList commandList = _renderer.CommandList;
        commandList.SetPipeline(_pipeline);
        commandList.SetVertexBuffer(0, _vertexBuffer);
        commandList.SetGraphicsResourceSet(0, _parametersSet);

        foreach (ScreenSpriteDrawCommand command in commands)
        {
            if (command.Texture.NativeResource is not TextureView textureView)
            {
                continue;
            }

            FillVertices(command, targetWidth, targetHeight, _vertices);
            commandList.UpdateBuffer(_vertexBuffer, 0, _vertices);
            commandList.UpdateBuffer(_parametersBuffer, 0, new SpriteParameters(
                Math.Max(targetWidth, 1), Math.Max(targetHeight, 1), Math.Clamp(command.Opacity, 0.0f, 1.0f), 0.0f));
            commandList.SetGraphicsResourceSet(1, GetTextureSet(textureView));
            commandList.Draw(MaxVerticesPerSprite);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (ResourceSet textureSet in _textureSets.Values)
        {
            textureSet.Dispose();
        }

        _textureSets.Clear();
        _pipeline.Dispose();
        _parametersSet.Dispose();
        _parametersLayout.Dispose();
        _textureLayout.Dispose();
        _parametersBuffer.Dispose();
        _vertexBuffer.Dispose();
        _sampler.Dispose();
        foreach (Shader shader in _shaders)
        {
            shader.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private ResourceSet GetTextureSet(TextureView textureView)
    {
        if (_textureSets.TryGetValue(textureView, out ResourceSet? resourceSet))
        {
            return resourceSet;
        }

        resourceSet = _renderer.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _textureLayout,
            textureView,
            _sampler));
        _textureSets[textureView] = resourceSet;
        return resourceSet;
    }

    private static void FillVertices(
        ScreenSpriteDrawCommand command,
        int targetWidth,
        int targetHeight,
        Span<float> vertices)
    {
        Vector2 center = (command.Min + command.Max) * 0.5f;
        Vector2 half = (command.Max - command.Min) * 0.5f;
        float radians = command.RotationDegrees * MathF.PI / 180.0f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        Vector2 Rotate(Vector2 local)
        {
            Vector2 position = center + new Vector2(
                (local.X * cos) - (local.Y * sin),
                (local.X * sin) + (local.Y * cos));
            return new Vector2(
                (position.X / Math.Max(targetWidth, 1) * 2.0f) - 1.0f,
                1.0f - (position.Y / Math.Max(targetHeight, 1) * 2.0f));
        }

        Vector2 p1 = Rotate(new Vector2(-half.X, -half.Y));
        Vector2 p2 = Rotate(new Vector2(half.X, -half.Y));
        Vector2 p3 = Rotate(new Vector2(half.X, half.Y));
        Vector2 p4 = Rotate(new Vector2(-half.X, half.Y));
        float topV = command.FlipV ? 1.0f : 0.0f;
        float bottomV = command.FlipV ? 0.0f : 1.0f;

        WriteVertex(vertices, 0, p1, 0.0f, topV);
        WriteVertex(vertices, 4, p2, 1.0f, topV);
        WriteVertex(vertices, 8, p3, 1.0f, bottomV);
        WriteVertex(vertices, 12, p1, 0.0f, topV);
        WriteVertex(vertices, 16, p3, 1.0f, bottomV);
        WriteVertex(vertices, 20, p4, 0.0f, bottomV);
    }

    private static void WriteVertex(Span<float> vertices, int offset, Vector2 position, float u, float v)
    {
        vertices[offset] = position.X;
        vertices[offset + 1] = position.Y;
        vertices[offset + 2] = u;
        vertices[offset + 3] = v;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct SpriteParameters(float TargetWidth, float TargetHeight, float Opacity, float Padding);

    private const string VertexShaderSource = """
        layout(set = 0, binding = 0, std140) uniform SpriteParameters
        {
            vec2 u_TargetSize;
            float u_Opacity;
            float u_Padding;
        } u_Parameters;

        layout(location = 0) in vec2 a_Position;
        layout(location = 1) in vec2 a_TexCoord;
        layout(location = 0) out vec2 v_TexCoord;

        void main()
        {
            gl_Position = vec4(a_Position, 0.0, 1.0);
            v_TexCoord = a_TexCoord;
        }
        """;

    private const string FragmentShaderSource = """
        layout(set = 0, binding = 0, std140) uniform SpriteParameters
        {
            vec2 u_TargetSize;
            float u_Opacity;
            float u_Padding;
        } u_Parameters;

        layout(set = 1, binding = 0) uniform texture2D u_Texture;
        layout(set = 1, binding = 1) uniform sampler u_Sampler;
        layout(location = 0) in vec2 v_TexCoord;
        layout(location = 0) out vec4 out_Color;

        void main()
        {
            vec4 color = texture(sampler2D(u_Texture, u_Sampler), v_TexCoord);
            out_Color = vec4(color.rgb, color.a * u_Parameters.u_Opacity);
        }
        """;
}
