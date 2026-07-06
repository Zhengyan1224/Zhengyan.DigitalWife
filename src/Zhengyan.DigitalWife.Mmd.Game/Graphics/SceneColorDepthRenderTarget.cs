using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public sealed unsafe class SceneColorDepthRenderTarget : IDisposable
{
    private readonly GL _gl;
    private bool _disposed;

    public SceneColorDepthRenderTarget(GL gl, string name)
    {
        _gl = gl;
        Name = name;
        FramebufferId = _gl.GenFramebuffer();
        ColorTextureId = _gl.GenTexture();
        DepthTextureId = _gl.GenTexture();
        StencilRenderbufferId = _gl.GenRenderbuffer();

        _gl.BindTexture(GLEnum.Texture2D, ColorTextureId);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);

        _gl.BindTexture(GLEnum.Texture2D, DepthTextureId);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.BindTexture(GLEnum.Texture2D, 0);
    }

    public string Name { get; }

    public uint FramebufferId { get; }

    public uint ColorTextureId { get; }

    public uint DepthTextureId { get; }

    public uint StencilRenderbufferId { get; }

    public bool StencilAvailable { get; private set; } = true;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public void EnsureSize(int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        if (Width == width && Height == height)
        {
            return;
        }

        Width = width;
        Height = height;

        _gl.BindTexture(GLEnum.Texture2D, ColorTextureId);
        _gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba, (uint)Width, (uint)Height, 0, GLEnum.Rgba, GLEnum.UnsignedByte, null);

        _gl.BindTexture(GLEnum.Texture2D, DepthTextureId);
        _gl.TexImage2D(
            GLEnum.Texture2D,
            0,
            (int)GLEnum.DepthComponent24,
            (uint)Width,
            (uint)Height,
            0,
            GLEnum.DepthComponent,
            GLEnum.UnsignedInt,
            null);
        _gl.BindTexture(GLEnum.Texture2D, 0);

        _gl.BindRenderbuffer(GLEnum.Renderbuffer, StencilRenderbufferId);
        _gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.StencilIndex8, (uint)Width, (uint)Height);
        _gl.BindRenderbuffer(GLEnum.Renderbuffer, 0);

        _gl.BindFramebuffer(GLEnum.Framebuffer, FramebufferId);
        _gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, ColorTextureId, 0);
        _gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.DepthAttachment, GLEnum.Texture2D, DepthTextureId, 0);
        _gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.StencilAttachment, GLEnum.Renderbuffer, StencilRenderbufferId);

        GLEnum status = _gl.CheckFramebufferStatus(GLEnum.Framebuffer);
        StencilAvailable = status == GLEnum.FramebufferComplete;
        if (!StencilAvailable)
        {
            _gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.StencilAttachment, GLEnum.Renderbuffer, 0);
            status = _gl.CheckFramebufferStatus(GLEnum.Framebuffer);
        }

        _gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"{Name} framebuffer is incomplete: {status}");
        }
    }

    public void Bind()
    {
        _gl.BindFramebuffer(GLEnum.Framebuffer, FramebufferId);
        _gl.Viewport(0, 0, (uint)Math.Max(Width, 1), (uint)Math.Max(Height, 1));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gl.DeleteFramebuffer(FramebufferId);
        _gl.DeleteTexture(ColorTextureId);
        _gl.DeleteTexture(DepthTextureId);
        _gl.DeleteRenderbuffer(StencilRenderbufferId);
        GC.SuppressFinalize(this);
    }
}
