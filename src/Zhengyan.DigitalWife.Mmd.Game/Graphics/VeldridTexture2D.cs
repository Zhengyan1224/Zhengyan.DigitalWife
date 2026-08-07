using Veldrid;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>Vulkan sampled texture owned by a <see cref="VulkanRenderer"/>.</summary>
public sealed class VeldridTexture2D : ITexture2D
{
    private readonly VulkanRenderer _renderer;
    private Texture? _texture;
    private TextureView? _view;
    private bool _disposed;

    public VeldridTexture2D(VulkanRenderer renderer)
    {
        _renderer = renderer;
    }

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool HasAlpha { get; private set; } = true;
    public TextureAlphaMode AlphaMode { get; private set; } = TextureAlphaMode.Opaque;
    public uint LegacyTextureId => 0;
    public object? NativeResource => _view;
    internal TextureView View => _view ?? throw new InvalidOperationException("Texture has not been uploaded.");

    public void LoadFromFile(string filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] rgba = Texture2D.DecodeRgba(filePath, out uint width, out uint height);
        Upload(rgba, width, height, Texture2D.DetermineAlphaMode(rgba));
    }

    public void Upload(byte[] bytes, uint width, uint height, TextureAlphaMode alphaMode = TextureAlphaMode.Blend)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (bytes.Length < width * height * 4)
        {
            throw new ArgumentException("RGBA upload data is smaller than width * height * 4.", nameof(bytes));
        }

        if (_texture is null || Width != width || Height != height)
        {
            DisposeResources();
            Width = checked((int)width);
            Height = checked((int)height);
            ResourceFactory factory = _renderer.ResourceFactory;
            _texture = factory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
            _view = factory.CreateTextureView(_texture);
        }

        AlphaMode = alphaMode;
        HasAlpha = alphaMode != TextureAlphaMode.Opaque;
        _renderer.Device.UpdateTexture(_texture, bytes, 0, 0, 0, width, height, 1, 0, 0);
    }

    public void Fill(byte red, byte green, byte blue, byte alpha = 255)
    {
        Upload([red, green, blue, alpha], 1, 1, alpha < 255 ? TextureAlphaMode.Blend : TextureAlphaMode.Opaque);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeResources();
        GC.SuppressFinalize(this);
    }

    private void DisposeResources()
    {
        _view?.Dispose();
        _view = null;
        _texture?.Dispose();
        _texture = null;
    }
}
