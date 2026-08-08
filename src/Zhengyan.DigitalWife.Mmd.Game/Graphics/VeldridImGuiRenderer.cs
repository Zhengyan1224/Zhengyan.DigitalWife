using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Silk.NET.Input;
using Veldrid;
using Veldrid.SPIRV;
using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal sealed unsafe class VeldridImGuiRenderer : IDisposable
{
    private const uint InitialVertexBufferSize = 10_000;
    private const uint InitialIndexBufferSize = 2_000;

    private readonly VulkanRenderer _renderer;
    private readonly nint _context;
    private readonly ResourceLayout _mainLayout;
    private readonly ResourceLayout _textureLayout;
    private readonly DeviceBuffer _frameBuffer;
    private readonly Sampler _sampler;
    private readonly ResourceSet _mainSet;
    private readonly Shader[] _shaders;
    private readonly ShaderSetDescription _shaderSet;
    private readonly Dictionary<TextureView, nint> _viewBindings = [];
    private readonly Dictionary<nint, ResourceSet> _textureSets = [];
    private readonly List<(OutputDescription Output, Pipeline Pipeline)> _pipelines = [];
    private readonly List<DeviceBuffer> _retiredBuffers = [];
    private DeviceBuffer _vertexBuffer;
    private DeviceBuffer _indexBuffer;
    private Texture? _fontTexture;
    private TextureView? _fontView;
    private uint _vertexBufferSize = InitialVertexBufferSize;
    private uint _indexBufferSize = InitialIndexBufferSize;
    private nint _nextBinding = 1;
    private bool _disposed;

    public VeldridImGuiRenderer(VulkanRenderer renderer, Action? configureFonts)
    {
        _renderer = renderer;
        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
        ImGui.StyleColorsDark();

        ImGuiIOPtr io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        configureFonts?.Invoke();

        ResourceFactory factory = renderer.ResourceFactory;
        _vertexBuffer = factory.CreateBuffer(new BufferDescription(_vertexBufferSize, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _indexBuffer = factory.CreateBuffer(new BufferDescription(_indexBufferSize, BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        _frameBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _sampler = factory.CreateSampler(SamplerDescription.Linear);
        _mainLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ImGuiFrame", ResourceKind.UniformBuffer, ShaderStages.Vertex),
            new ResourceLayoutElementDescription("ImGuiSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        _textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ImGuiTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment)));
        _mainSet = factory.CreateResourceSet(new ResourceSetDescription(_mainLayout, _frameBuffer, _sampler));
        _shaders = factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("imgui.vert", VertexSource, ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("imgui.frag", FragmentSource, ShaderStages.Fragment));
        _shaderSet = new ShaderSetDescription(
            [new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float2),
                new VertexElementDescription("Uv", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Byte4_Norm))],
            _shaders);

        RecreateFontTexture();
    }

    public void Update(
        float deltaSeconds,
        int width,
        int height,
        IMouse? mouse,
        IKeyboard? keyboard,
        float wheelX,
        float wheelY,
        IReadOnlyList<char> characters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ImGui.SetCurrentContext(_context);
        ImGuiIOPtr io = ImGui.GetIO();
        io.DeltaTime = deltaSeconds;
        io.DisplaySize = new Vector2(width, height);
        io.DisplayFramebufferScale = Vector2.One;

        Vector2 position = mouse?.Position ?? new Vector2(-float.MaxValue, -float.MaxValue);
        io.AddMousePosEvent(position.X, position.Y);
        io.AddMouseButtonEvent(0, mouse?.IsButtonPressed(SilkMouseButton.Left) == true);
        io.AddMouseButtonEvent(1, mouse?.IsButtonPressed(SilkMouseButton.Right) == true);
        io.AddMouseButtonEvent(2, mouse?.IsButtonPressed(SilkMouseButton.Middle) == true);
        if (wheelX != 0 || wheelY != 0) io.AddMouseWheelEvent(wheelX, wheelY);

        foreach ((SilkKey silkKey, ImGuiKey imguiKey) in KeyMap)
            io.AddKeyEvent(imguiKey, keyboard?.IsKeyPressed(silkKey) == true);
        io.AddKeyEvent(ImGuiKey.ModCtrl, IsDown(keyboard, SilkKey.ControlLeft, SilkKey.ControlRight));
        io.AddKeyEvent(ImGuiKey.ModShift, IsDown(keyboard, SilkKey.ShiftLeft, SilkKey.ShiftRight));
        io.AddKeyEvent(ImGuiKey.ModAlt, IsDown(keyboard, SilkKey.AltLeft, SilkKey.AltRight));
        io.AddKeyEvent(ImGuiKey.ModSuper, IsDown(keyboard, SilkKey.SuperLeft, SilkKey.SuperRight));
        foreach (char character in characters) io.AddInputCharacter(character);

        ImGui.NewFrame();
    }

    public void Render()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_renderer.IsFrameOpen) return;
        ImGui.SetCurrentContext(_context);
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
    }

    public nint GetOrCreateTextureBinding(TextureView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_viewBindings.TryGetValue(view, out nint existing)) return existing;
        nint binding = _nextBinding++;
        ResourceSet set = _renderer.ResourceFactory.CreateResourceSet(
            new ResourceSetDescription(_textureLayout, view));
        _viewBindings.Add(view, binding);
        _textureSets.Add(binding, set);
        return binding;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ImGui.SetCurrentContext(_context);
        foreach (ResourceSet set in _textureSets.Values) set.Dispose();
        foreach ((_, Pipeline pipeline) in _pipelines) pipeline.Dispose();
        foreach (Shader shader in _shaders) shader.Dispose();
        _fontView?.Dispose();
        _fontTexture?.Dispose();
        _mainSet.Dispose();
        _textureLayout.Dispose();
        _mainLayout.Dispose();
        _sampler.Dispose();
        _frameBuffer.Dispose();
        _indexBuffer.Dispose();
        _vertexBuffer.Dispose();
        foreach (DeviceBuffer buffer in _retiredBuffers) buffer.Dispose();
        ImGui.DestroyContext(_context);
    }

    private void RecreateFontTexture()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height, out int bytesPerPixel);
        uint byteCount = checked((uint)(width * height * bytesPerPixel));
        ResourceFactory factory = _renderer.ResourceFactory;
        _fontTexture = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)width, (uint)height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        _renderer.NativeDevice.UpdateTexture(
            _fontTexture, (nint)pixels, byteCount,
            0, 0, 0, (uint)width, (uint)height, 1, 0, 0);
        _fontView = factory.CreateTextureView(_fontTexture);
        nint binding = GetOrCreateTextureBinding(_fontView);
        io.Fonts.SetTexID(binding);
        io.Fonts.ClearTexData();
    }

    private void RenderDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0 || drawData.TotalVtxCount == 0) return;
        EnsureBufferCapacity(drawData.TotalVtxCount, drawData.TotalIdxCount);

        CommandList commands = _renderer.NativeCommandList;
        uint vertexOffsetBytes = 0;
        uint indexOffsetBytes = 0;
        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr drawList = drawData.CmdLists[i];
            uint vertexBytes = checked((uint)(drawList.VtxBuffer.Size * sizeof(ImDrawVert)));
            uint indexBytes = checked((uint)(drawList.IdxBuffer.Size * sizeof(ushort)));
            commands.UpdateBuffer(_vertexBuffer, vertexOffsetBytes, (nint)drawList.VtxBuffer.Data, vertexBytes);
            commands.UpdateBuffer(_indexBuffer, indexOffsetBytes, (nint)drawList.IdxBuffer.Data, indexBytes);
            vertexOffsetBytes += vertexBytes;
            indexOffsetBytes += indexBytes;
        }

        FrameData frame = new()
        {
            Scale = new Vector2(2f / drawData.DisplaySize.X, 2f / drawData.DisplaySize.Y),
            Translate = new Vector2(
                -1f - drawData.DisplayPos.X * (2f / drawData.DisplaySize.X),
                -1f - drawData.DisplayPos.Y * (2f / drawData.DisplaySize.Y))
        };
        _renderer.NativeDevice.UpdateBuffer(_frameBuffer, 0, frame);
        commands.SetPipeline(GetPipeline(_renderer.CurrentOutputDescription));
        commands.SetVertexBuffer(0, _vertexBuffer);
        commands.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
        commands.SetGraphicsResourceSet(0, _mainSet);

        Vector2 clipOffset = drawData.DisplayPos;
        Vector2 clipScale = drawData.FramebufferScale;
        int framebufferWidth = Math.Max((int)(drawData.DisplaySize.X * clipScale.X), 0);
        int framebufferHeight = Math.Max((int)(drawData.DisplaySize.Y * clipScale.Y), 0);
        if (framebufferWidth == 0 || framebufferHeight == 0) return;
        int globalVertexOffset = 0;
        uint globalIndexOffset = 0;
        for (int listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            ImDrawListPtr drawList = drawData.CmdLists[listIndex];
            for (int commandIndex = 0; commandIndex < drawList.CmdBuffer.Size; commandIndex++)
            {
                ImDrawCmdPtr drawCommand = drawList.CmdBuffer[commandIndex];
                if (drawCommand.UserCallback != nint.Zero) continue;
                if (!_textureSets.TryGetValue(drawCommand.TextureId, out ResourceSet? textureSet)) continue;

                Vector4 clip = drawCommand.ClipRect;
                float left = (clip.X - clipOffset.X) * clipScale.X;
                float top = (clip.Y - clipOffset.Y) * clipScale.Y;
                float right = (clip.Z - clipOffset.X) * clipScale.X;
                float bottom = (clip.W - clipOffset.Y) * clipScale.Y;
                if (right <= left || bottom <= top) continue;

                int x = Math.Max((int)MathF.Floor(left), 0);
                int y = Math.Max((int)MathF.Floor(top), 0);
                int clipRight = Math.Min((int)MathF.Ceiling(right), framebufferWidth);
                int clipBottom = Math.Min((int)MathF.Ceiling(bottom), framebufferHeight);
                if (clipRight <= x || clipBottom <= y) continue;

                commands.SetScissorRect(
                    0,
                    (uint)x,
                    (uint)y,
                    (uint)(clipRight - x),
                    (uint)(clipBottom - y));
                commands.SetGraphicsResourceSet(1, textureSet);
                commands.DrawIndexed(
                    drawCommand.ElemCount,
                    1,
                    globalIndexOffset + drawCommand.IdxOffset,
                    globalVertexOffset + (int)drawCommand.VtxOffset,
                    0);
            }

            globalIndexOffset += (uint)drawList.IdxBuffer.Size;
            globalVertexOffset += drawList.VtxBuffer.Size;
        }
    }

    private void EnsureBufferCapacity(int vertexCount, int indexCount)
    {
        uint requiredVertices = checked((uint)(vertexCount * sizeof(ImDrawVert)));
        uint requiredIndices = checked((uint)(indexCount * sizeof(ushort)));
        if (requiredVertices > _vertexBufferSize)
        {
            _retiredBuffers.Add(_vertexBuffer);
            _vertexBufferSize = Math.Max(requiredVertices * 3 / 2, InitialVertexBufferSize);
            _vertexBuffer = _renderer.ResourceFactory.CreateBuffer(
                new BufferDescription(_vertexBufferSize, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        }
        if (requiredIndices > _indexBufferSize)
        {
            _retiredBuffers.Add(_indexBuffer);
            _indexBufferSize = Math.Max(requiredIndices * 3 / 2, InitialIndexBufferSize);
            _indexBuffer = _renderer.ResourceFactory.CreateBuffer(
                new BufferDescription(_indexBufferSize, BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        }
    }

    private Pipeline GetPipeline(OutputDescription output)
    {
        foreach ((OutputDescription candidate, Pipeline pipeline) in _pipelines)
            if (candidate.Equals(output)) return pipeline;
        RasterizerStateDescription rasterizer = new(
            FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise,
            depthClipEnabled: true, scissorTestEnabled: true);
        Pipeline created = _renderer.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            DepthStencilStateDescription.Disabled,
            rasterizer,
            PrimitiveTopology.TriangleList,
            _shaderSet,
            [_mainLayout, _textureLayout],
            output));
        _pipelines.Add((output, created));
        return created;
    }

    private static bool IsDown(IKeyboard? keyboard, SilkKey first, SilkKey second) =>
        keyboard?.IsKeyPressed(first) == true || keyboard?.IsKeyPressed(second) == true;

    private static readonly (SilkKey Silk, ImGuiKey ImGui)[] KeyMap =
    [
        (SilkKey.Tab, ImGuiKey.Tab), (SilkKey.Left, ImGuiKey.LeftArrow), (SilkKey.Right, ImGuiKey.RightArrow),
        (SilkKey.Up, ImGuiKey.UpArrow), (SilkKey.Down, ImGuiKey.DownArrow), (SilkKey.PageUp, ImGuiKey.PageUp),
        (SilkKey.PageDown, ImGuiKey.PageDown), (SilkKey.Home, ImGuiKey.Home), (SilkKey.End, ImGuiKey.End),
        (SilkKey.Insert, ImGuiKey.Insert), (SilkKey.Delete, ImGuiKey.Delete), (SilkKey.Backspace, ImGuiKey.Backspace),
        (SilkKey.Space, ImGuiKey.Space), (SilkKey.Enter, ImGuiKey.Enter), (SilkKey.Escape, ImGuiKey.Escape),
        (SilkKey.A, ImGuiKey.A), (SilkKey.C, ImGuiKey.C), (SilkKey.V, ImGuiKey.V),
        (SilkKey.X, ImGuiKey.X), (SilkKey.Y, ImGuiKey.Y), (SilkKey.Z, ImGuiKey.Z)
    ];

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameData
    {
        public Vector2 Scale;
        public Vector2 Translate;
    }

    private const string VertexSource = """
        layout(set=0,binding=0,std140) uniform ImGuiFrame { vec2 scale; vec2 translate; } frame;
        layout(location=0) in vec2 in_Position;
        layout(location=1) in vec2 in_Uv;
        layout(location=2) in vec4 in_Color;
        layout(location=0) out vec2 fs_Uv;
        layout(location=1) out vec4 fs_Color;
        void main()
        {
            vec2 position = in_Position * frame.scale + frame.translate;
            gl_Position = vec4(position.x, -position.y, 0, 1);
            fs_Uv = in_Uv;
            fs_Color = in_Color;
        }
        """;

    private const string FragmentSource = """
        layout(set=0,binding=1) uniform sampler imguiSampler;
        layout(set=1,binding=0) uniform texture2D imguiTexture;
        layout(location=0) in vec2 fs_Uv;
        layout(location=1) in vec4 fs_Color;
        layout(location=0) out vec4 out_Color;
        void main()
        {
            out_Color = fs_Color * texture(sampler2D(imguiTexture, imguiSampler), fs_Uv);
        }
        """;
}
