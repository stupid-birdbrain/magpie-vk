using System;
using Magpie.Core;

namespace Magpie;

public sealed class SpriteTexture : IDisposable {
    private Image _image;
    private DeviceMemory _memory;
    private ImageView _imageView;
    private Sampler _sampler;
    private bool _disposed;

    public uint Width => _image.Width;
    public uint Height => _image.Height;

    public ref readonly Image Image => ref _image;
    public ImageView ImageView => _imageView;
    public Sampler Sampler => _sampler;

    public SpriteTexture(Image image, DeviceMemory memory, ImageView imageView, Sampler sampler) {
        _image = image;
        _memory = memory;
        _imageView = imageView;
        _sampler = sampler;
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _sampler.Dispose();
        _imageView.Dispose();
        _memory.Dispose();
        _image.Dispose();
        _disposed = true;
    }
}
