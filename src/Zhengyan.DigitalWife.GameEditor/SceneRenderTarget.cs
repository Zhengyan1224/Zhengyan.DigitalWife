using System.Numerics;
using Silk.NET.OpenGLES;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed class SceneRenderTarget : IDisposable
{
    private readonly IRenderTarget _target;
    private readonly GraphicsDevice _graphicsDevice;

    public SceneRenderTarget(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
        _target = graphicsDevice.CreateRenderTarget("EditorScene");
    }

    public uint FramebufferId => _target is RenderTexture glTarget ? glTarget.FramebufferId : 0;

    public uint ColorTextureId => _target.LegacyColorTextureId;

    public uint DepthStencilRenderbufferId => _target is RenderTexture glTarget ? glTarget.DepthStencilRenderbufferId : 0;

    public int Width => _target.Width;

    public int Height => _target.Height;

    public void EnsureSize(int width, int height)
    {
        _target.EnsureSize(width, height);
    }

    public void Bind()
    {
        if (_target is RenderTexture glTarget)
        {
            glTarget.Bind();
        }
        else
        {
            _target.BeginPass(Vector4.Zero);
        }
    }

    public void Unbind(int backBufferWidth, int backBufferHeight)
    {
        if (_target is RenderTexture glTarget)
        {
            glTarget.EndPass();
            _graphicsDevice.Gl.Viewport(0, 0, (uint)Math.Max(backBufferWidth, 1), (uint)Math.Max(backBufferHeight, 1));
        }
        else
        {
            _target.EndPass();
        }
    }

    public void ForceOpaqueAlpha()
    {
        if (_target is not RenderTexture glTarget)
        {
            return;
        }

        GL gl = _graphicsDevice.Gl;
        gl.BindFramebuffer(GLEnum.Framebuffer, glTarget.FramebufferId);
        gl.Disable(GLEnum.ScissorTest);
        gl.ColorMask(false, false, false, true);
        gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        gl.ColorMask(true, true, true, true);
    }

    public void Dispose()
    {
        _target.Dispose();
        GC.SuppressFinalize(this);
    }
}
