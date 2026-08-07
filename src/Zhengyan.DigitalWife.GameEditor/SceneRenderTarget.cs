using System.Numerics;
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

    public RuntimeTextureHandle ColorTextureHandle => new(_target.Backend, _target.LegacyColorTextureId, _target.NativeColorResource);

    public uint DepthStencilRenderbufferId => _target is RenderTexture glTarget ? glTarget.DepthStencilRenderbufferId : 0;

    public int Width => _target.Width;

    public int Height => _target.Height;

    public void EnsureSize(int width, int height)
    {
        _target.EnsureSize(width, height);
    }

    public void Bind()
    {
        _target.ResumePass();
    }

    public void BeginPass(Vector4 clearColor)
    {
        _target.BeginPass(clearColor);
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
        _target.ForceOpaqueAlpha();
    }

    public void Dispose()
    {
        _target.Dispose();
        GC.SuppressFinalize(this);
    }
}
