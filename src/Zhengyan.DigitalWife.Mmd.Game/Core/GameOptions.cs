using System.Numerics;
using Silk.NET.Maths;

using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.Mmd.Game;

public enum AnimationTimingMode
{
    FrameRateDependent = 0,
    TimeSynchronized = 1
}

public sealed class GameOptions
{
    public GraphicsBackend GraphicsBackend { get; set; } = GraphicsBackend.Auto;

    public string Title { get; set; } = "Zhengyan.DigitalWife.Mmd.Game";

    public Vector2D<int> WindowSize { get; set; } = new(1280, 720);

    public bool IsFullscreen { get; set; }

    public bool IsResizable { get; set; } = true;

    public bool IsTopMost { get; set; }

    public bool TransparentFramebuffer { get; set; }

    public bool HideWindowBorder { get; set; }

    public bool VSync { get; set; } = true;

    public int Samples { get; set; } = 4;

    public int PreferredDepthBufferBits { get; set; } = 24;

    public int PreferredStencilBufferBits { get; set; } = 8;

    public Vector4 ClearColor { get; set; } = new(0.08f, 0.09f, 0.12f, 1.0f);

    public bool UseOpenCL { get; set; } = true;

    public bool UseVulkanCompute { get; set; } = true;

    public bool EnableAudio { get; set; } = true;

    public AnimationTimingMode AnimationTimingMode { get; set; } = AnimationTimingMode.FrameRateDependent;
}

