using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public sealed class GraphicsDevice
{
    public GraphicsDevice(GL gl, Vector4 clearColor, Vector2D<int> backBufferSize)
    {
        Gl = gl;
        ClearColor = clearColor;
        BackBufferSize = backBufferSize;

        // The default framebuffer viewport is not guaranteed to match the window size.
        // Initialize it up front so the first non-ImGui draw call can render correctly.
        Gl.Viewport(backBufferSize);
    }

    public GL Gl { get; }

    public Vector4 ClearColor { get; set; }

    public Vector2D<int> BackBufferSize { get; private set; }

    public void Resize(Vector2D<int> backBufferSize)
    {
        BackBufferSize = backBufferSize;
        Gl.Viewport(backBufferSize);
    }

    public void Clear(ClearBufferMask mask = ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit)
    {
        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.Viewport(BackBufferSize);
        Gl.Disable(GLEnum.ScissorTest);
        Gl.Disable(GLEnum.StencilTest);
        Gl.ColorMask(true, true, true, true);
        Gl.DepthMask(true);
        Gl.StencilMask(0xFF);
        Gl.ClearColor(ClearColor.X, ClearColor.Y, ClearColor.Z, ClearColor.W);
        Gl.Clear(mask);
    }
}

