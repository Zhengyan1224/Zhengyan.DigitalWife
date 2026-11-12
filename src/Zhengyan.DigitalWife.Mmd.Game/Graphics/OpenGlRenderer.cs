using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.OpenGLES;
using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public sealed class OpenGlRenderer : IRenderer
{
    private GL? _gl;
    private readonly IRenderBackendServices _services;
    private int _defaultSamples = 1;
    private bool _runtimeSamplesSet, _useSceneTarget;
    private RenderTexture? _sceneTarget;
    private OpenGlScenePresenter? _presenter;
    private HashSet<int> _supportedSamples = [1];

    public OpenGlRenderer()
    {
        _services = new OpenGlRenderBackendServices(this);
    }

    public GraphicsBackend Backend => GraphicsBackend.OpenGL;

    public string Name => "OpenGL ES 3.0";

    public int RequestedAntiAliasingSamples { get; private set; } = 1;

    public int AntiAliasingSamples { get; private set; } = 1;

    public IRenderBackendServices Services => _services;

    public Vector2D<int> BackBufferSize { get; private set; }

    internal GL Gl => _gl ?? throw new InvalidOperationException("The OpenGL renderer has not been initialized.");

    public void Initialize(IWindow window, Vector2D<int> backBufferSize, int requestedSamples)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_gl is not null)
        {
            throw new InvalidOperationException("The OpenGL renderer is already initialized.");
        }

        Initialize(window.CreateOpenGLES(), backBufferSize, requestedSamples);
    }

    /// <summary>
    /// Attaches to a current GLES context owned by a platform host. The renderer
    /// owns the GL bindings; the host retains the context and presents its surface.
    /// </summary>
    public void Initialize(GL gl, Vector2D<int> backBufferSize, int requestedSamples)
    {
        ArgumentNullException.ThrowIfNull(gl);
        if (_gl is not null)
        {
            throw new InvalidOperationException("The OpenGL renderer is already initialized.");
        }

        RequestedAntiAliasingSamples = Zhengyan.DigitalWife.Mmd.Game.Graphics.AntiAliasingSamples.NormalizeRequested(requestedSamples);
        _gl = gl;
        int actualSamples = 1;
        try
        {
            actualSamples = Math.Max(1, Gl.GetInteger(GLEnum.Samples));
        }
        catch
        {
            // Some GLES implementations do not expose the default framebuffer sample count.
        }

        AntiAliasingSamples = Zhengyan.DigitalWife.Mmd.Game.Graphics.AntiAliasingSamples.FallbackToSupported(
            RequestedAntiAliasingSamples, actualSamples);
        _defaultSamples = actualSamples;
        _supportedSamples = QuerySamples(GLEnum.Rgba8);
        _supportedSamples.IntersectWith(QuerySamples(GLEnum.Depth24Stencil8));
        _supportedSamples.Add(1);
        Resize(backBufferSize);
        SetAntiAliasingSamples(RequestedAntiAliasingSamples);
    }

    public void SetAntiAliasingSamples(int requestedSamples)
    {
        requestedSamples = Zhengyan.DigitalWife.Mmd.Game.Graphics.AntiAliasingSamples.NormalizeRequested(requestedSamples);
        if (_runtimeSamplesSet && RequestedAntiAliasingSamples == requestedSamples) return;
        _runtimeSamplesSet = true;
        RequestedAntiAliasingSamples = requestedSamples;
        int actual = _supportedSamples.Where(value => value <= requestedSamples).DefaultIfEmpty(1).Max();
        _useSceneTarget = actual != _defaultSamples;
        if (_useSceneTarget)
        {
            _sceneTarget ??= new RenderTexture(Gl,"RuntimeScene");
            _sceneTarget.EnsureSize(BackBufferSize.X,BackBufferSize.Y,actual);
            _presenter ??= new OpenGlScenePresenter(Gl);
            actual = _sceneTarget.SampleCount;
        }
        AntiAliasingSamples = actual;
    }

    private unsafe HashSet<int> QuerySamples(GLEnum format)
    {
        int count=0;
        Gl.GetInternalformat(GLEnum.Renderbuffer,format,(GLEnum)0x9380,1,&count);
        count=Math.Clamp(count,0,32); int* values=stackalloc int[32];
        if(count>0) Gl.GetInternalformat(GLEnum.Renderbuffer,format,GLEnum.Samples,(uint)count,values);
        HashSet<int> supported=[1];
        for(int i=0;i<count;i++) if(Zhengyan.DigitalWife.Mmd.Game.Graphics.AntiAliasingSamples.Choices.Contains(values[i])) supported.Add(values[i]);
        return supported;
    }

    public void Resize(Vector2D<int> backBufferSize)
    {
        BackBufferSize = backBufferSize;
        if (_useSceneTarget)
        {
            _sceneTarget!.EnsureSize(backBufferSize.X,backBufferSize.Y,AntiAliasingSamples);
            AntiAliasingSamples = _sceneTarget.SampleCount;
        }
        Gl.Viewport(backBufferSize);
    }

    public void Clear(Vector4 color)
    {
        GL gl = Gl;
        if (_useSceneTarget) _sceneTarget!.Bind();
        else gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        gl.Viewport(BackBufferSize);
        gl.Disable(GLEnum.ScissorTest);
        gl.Disable(GLEnum.StencilTest);
        gl.ColorMask(true, true, true, true);
        gl.DepthMask(true);
        gl.StencilMask(0xFF);
        gl.ClearColor(color.X, color.Y, color.Z, color.W);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
    }

    public void ClearViewport(int x, int y, int width, int height, Vector4 color)
    {
        GL gl = Gl;
        gl.Enable(GLEnum.ScissorTest);
        gl.Scissor(x, y, (uint)Math.Max(width, 1), (uint)Math.Max(height, 1));
        gl.ColorMask(true, true, true, true);
        gl.DepthMask(true);
        gl.StencilMask(0xFF);
        gl.ClearColor(color.X, color.Y, color.Z, color.W);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
    }

    public IRenderTarget CreateRenderTarget(string name)
    {
        return new RenderTexture(Gl, name);
    }

    public ITexture2D CreateTexture2D()
    {
        return new Texture2D(Gl);
    }

    public IScreenSpriteRenderer CreateScreenSpriteRenderer()
    {
        return new ScreenSpriteRenderer(Gl);
    }

    public IGpuBuffer CreateBuffer(GpuBufferDescription description)
    {
        return new OpenGlGpuBuffer(Gl, description);
    }

    public IGpuSampler CreateSampler(GpuSamplerDescription description)
    {
        return new OpenGlGpuSampler(Gl, description);
    }

    public void RestoreBackBuffer()
    {
        if (_useSceneTarget) _sceneTarget!.Bind();
        else Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.Viewport(BackBufferSize);
    }

    public void SetViewport(int x, int y, int width, int height)
        => Gl.Viewport(x, y, (uint)Math.Max(width, 1), (uint)Math.Max(height, 1));

    public void SetScissor(int x, int y, int width, int height, bool enabled)
    {
        if (!enabled)
        {
            Gl.Disable(GLEnum.ScissorTest);
            return;
        }

        Gl.Enable(GLEnum.ScissorTest);
        Gl.Scissor(x, y, (uint)Math.Max(width, 1), (uint)Math.Max(height, 1));
    }

    public unsafe bool TryReadBackBufferRgba(Span<byte> destination)
    {
        int required = checked(Math.Max(BackBufferSize.X, 1) * Math.Max(BackBufferSize.Y, 1) * 4);
        if (destination.Length < required)
        {
            return false;
        }

        fixed (byte* pixels = destination)
        {
            uint previous = (uint)Gl.GetInteger(GLEnum.FramebufferBinding);
            if (_useSceneTarget) _sceneTarget!.EndPass();
            Gl.BindFramebuffer(GLEnum.Framebuffer, _useSceneTarget ? _sceneTarget!.FramebufferId : 0);
            Gl.PixelStore(GLEnum.PackAlignment, 1);
            Gl.ReadPixels(0, 0, (uint)Math.Max(BackBufferSize.X, 1), (uint)Math.Max(BackBufferSize.Y, 1),
                GLEnum.Rgba, GLEnum.UnsignedByte, pixels);
            Gl.BindFramebuffer(GLEnum.Framebuffer, previous);
        }

        return true;
    }

    public void Present()
    {
        if (_useSceneTarget)
        {
            _sceneTarget!.EndPass();
            _presenter!.Draw(_sceneTarget.ColorTextureId,Math.Max(1,BackBufferSize.X),Math.Max(1,BackBufferSize.Y));
        }
    }

    public void WaitForIdle() => Gl.Finish();

    public void Dispose()
    {
        _presenter?.Dispose(); _presenter=null;
        _sceneTarget?.Dispose(); _sceneTarget=null;
        _gl?.Dispose();
        _gl = null;
    }
}
