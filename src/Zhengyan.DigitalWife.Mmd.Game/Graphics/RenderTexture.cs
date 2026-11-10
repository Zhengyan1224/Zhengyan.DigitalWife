using Silk.NET.OpenGLES;
using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public sealed unsafe class RenderTexture : IRenderTarget
{
    private readonly GL _gl;
    private bool _disposed;
    private uint _multisampleFramebuffer;
    private uint _multisampleColor;
    private uint _multisampleDepth;
    private int _requestedSamples = 1;

    public RenderTexture(GL gl, string name)
    {
        _gl = gl;
        Name = name;
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

    public string Name { get; }

    public GraphicsBackend Backend => GraphicsBackend.OpenGL;

    public uint LegacyColorTextureId => ColorTextureId;

    public object NativeColorResource => ColorTextureId;

    public object? NativeDepthResource => null;

    public uint FramebufferId { get; }

    public uint ColorTextureId { get; }

    public uint DepthStencilRenderbufferId { get; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int SampleCount { get; private set; } = 1;

    public void EnsureSize(int width, int height)
        => EnsureSize(width, height, _requestedSamples);

    public void EnsureSize(int width, int height, int samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        samples = AntiAliasingSamples.NormalizeRequested(samples);
        if (Width == width && Height == height && _requestedSamples == samples)
        {
            return;
        }

        Width = width;
        Height = height;
        _requestedSamples = samples;
        DeleteMultisampleResources();
        SampleCount = 1;

        _gl.BindTexture(GLEnum.Texture2D, ColorTextureId);
        _gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8, (uint)Width, (uint)Height, 0, GLEnum.Rgba, GLEnum.UnsignedByte, null);
        _gl.BindTexture(GLEnum.Texture2D, 0);

        _gl.BindRenderbuffer(GLEnum.Renderbuffer, DepthStencilRenderbufferId);
        _gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.Depth24Stencil8, (uint)Width, (uint)Height);
        _gl.BindRenderbuffer(GLEnum.Renderbuffer, 0);

        _gl.BindFramebuffer(GLEnum.Framebuffer, FramebufferId);
        _gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, ColorTextureId, 0);
        _gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.DepthStencilAttachment, GLEnum.Renderbuffer, DepthStencilRenderbufferId);
        if (_gl.CheckFramebufferStatus(GLEnum.Framebuffer) != GLEnum.FramebufferComplete)
            throw new InvalidOperationException($"Render target '{Name}' is incomplete.");

        if (samples > 1)
        {
            HashSet<int> colorSamples = GetSupportedSamples(GLEnum.Rgba8);
            HashSet<int> depthSamples = GetSupportedSamples(GLEnum.Depth24Stencil8);
            foreach (int candidate in AntiAliasingSamples.Choices.Reverse())
            {
                if (candidate <= 1 || candidate > samples
                    || !colorSamples.Contains(candidate) || !depthSamples.Contains(candidate)) continue;
                _multisampleFramebuffer = _gl.GenFramebuffer();
                _multisampleColor = _gl.GenRenderbuffer();
                _multisampleDepth = _gl.GenRenderbuffer();
                _gl.BindFramebuffer(GLEnum.Framebuffer, _multisampleFramebuffer);
                _gl.BindRenderbuffer(GLEnum.Renderbuffer, _multisampleColor);
                _gl.RenderbufferStorageMultisample(GLEnum.Renderbuffer, (uint)candidate, GLEnum.Rgba8, (uint)Width, (uint)Height);
                _gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.ColorAttachment0, GLEnum.Renderbuffer, _multisampleColor);
                _gl.BindRenderbuffer(GLEnum.Renderbuffer, _multisampleDepth);
                _gl.RenderbufferStorageMultisample(GLEnum.Renderbuffer, (uint)candidate, GLEnum.Depth24Stencil8, (uint)Width, (uint)Height);
                _gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.DepthStencilAttachment, GLEnum.Renderbuffer, _multisampleDepth);
                if (_gl.CheckFramebufferStatus(GLEnum.Framebuffer) == GLEnum.FramebufferComplete)
                {
                    SampleCount = candidate;
                    break;
                }
                DeleteMultisampleResources();
            }
        }
        _gl.BindRenderbuffer(GLEnum.Renderbuffer, 0);
        _gl.BindFramebuffer(GLEnum.Framebuffer, 0);
    }

    private HashSet<int> GetSupportedSamples(GLEnum format)
    {
        int count = 0;
        _gl.GetInternalformat(GLEnum.Renderbuffer, format, (GLEnum)0x9380, 1, &count); // NUM_SAMPLE_COUNTS
        count = Math.Clamp(count, 0, 32);
        int* values = stackalloc int[32];
        if (count > 0) _gl.GetInternalformat(GLEnum.Renderbuffer, format, GLEnum.Samples, (uint)count, values);
        HashSet<int> result = [];
        for (int i = 0; i < count; i++) result.Add(values[i]);
        return result;
    }

    public void Bind()
    {
        _gl.BindFramebuffer(GLEnum.Framebuffer, _multisampleFramebuffer != 0 ? _multisampleFramebuffer : FramebufferId);
        _gl.Viewport(0, 0, (uint)Math.Max(Width, 1), (uint)Math.Max(Height, 1));
    }

    public void BeginPass(Vector4 clearColor)
    {
        Bind();
        _gl.Disable(GLEnum.ScissorTest);
        _gl.Disable(GLEnum.StencilTest);
        _gl.ColorMask(true, true, true, true);
        _gl.DepthMask(true);
        _gl.StencilMask(0xFF);
        _gl.ClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
    }

    public void ResumePass()
    {
        Bind();
    }

    public void EndPass()
    {
        if (SampleCount > 1)
        {
            _gl.Disable(GLEnum.ScissorTest);
            _gl.BindFramebuffer(GLEnum.ReadFramebuffer, _multisampleFramebuffer);
            _gl.BindFramebuffer(GLEnum.DrawFramebuffer, FramebufferId);
            _gl.BlitFramebuffer(0, 0, Width, Height, 0, 0, Width, Height, ClearBufferMask.ColorBufferBit, GLEnum.Nearest);
        }
        _gl.BindFramebuffer(GLEnum.Framebuffer, 0);
    }

    public void ForceOpaqueAlpha()
    {
        Bind();
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
        DeleteMultisampleResources();
        _gl.DeleteFramebuffer(FramebufferId);
        _gl.DeleteTexture(ColorTextureId);
        _gl.DeleteRenderbuffer(DepthStencilRenderbufferId);
        GC.SuppressFinalize(this);
    }

    private void DeleteMultisampleResources()
    {
        if (_multisampleFramebuffer != 0) _gl.DeleteFramebuffer(_multisampleFramebuffer);
        if (_multisampleColor != 0) _gl.DeleteRenderbuffer(_multisampleColor);
        if (_multisampleDepth != 0) _gl.DeleteRenderbuffer(_multisampleDepth);
        _multisampleFramebuffer = _multisampleColor = _multisampleDepth = 0;
    }
}
