using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Samples.GameEditor;

internal sealed unsafe class SceneRenderTarget : IDisposable
{
    private readonly GL _gl;
    private bool _disposed;

    public SceneRenderTarget(GL gl)
    {
        _gl = gl;
        FramebufferId = _gl.GenFramebuffer();
        ColorTextureId = _gl.GenTexture();
        DepthStencilRenderbufferId = _gl.GenRenderbuffer();

        _gl.BindTexture(GLEnum.Texture2D, ColorTextureId);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.BindTexture(GLEnum.Texture2D, 0);
    }

    public uint FramebufferId { get; }

    public uint ColorTextureId { get; }

    public uint DepthStencilRenderbufferId { get; }

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
        _gl.BindTexture(GLEnum.Texture2D, 0);

        _gl.BindRenderbuffer(GLEnum.Renderbuffer, DepthStencilRenderbufferId);
        _gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.Depth24Stencil8, (uint)Width, (uint)Height);
        _gl.BindRenderbuffer(GLEnum.Renderbuffer, 0);

        _gl.BindFramebuffer(GLEnum.Framebuffer, FramebufferId);
        _gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, ColorTextureId, 0);
        _gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.DepthStencilAttachment, GLEnum.Renderbuffer, DepthStencilRenderbufferId);
        _gl.BindFramebuffer(GLEnum.Framebuffer, 0);
    }

    public void Bind()
    {
        _gl.BindFramebuffer(GLEnum.Framebuffer, FramebufferId);
        _gl.Viewport(0, 0, (uint)Math.Max(Width, 1), (uint)Math.Max(Height, 1));
    }

    public void Unbind(int backBufferWidth, int backBufferHeight)
    {
        _gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)Math.Max(backBufferWidth, 1), (uint)Math.Max(backBufferHeight, 1));
    }

    public void ForceOpaqueAlpha()
    {
        _gl.BindFramebuffer(GLEnum.Framebuffer, FramebufferId);
        _gl.Disable(GLEnum.ScissorTest);
        _gl.ColorMask(false, false, false, true);
        _gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.ColorMask(true, true, true, true);
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
        _gl.DeleteRenderbuffer(DepthStencilRenderbufferId);
        GC.SuppressFinalize(this);
    }
}
