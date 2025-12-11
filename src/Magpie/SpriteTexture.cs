using System;
using Magpie.Core;

namespace Magpie;

public sealed class SpriteTexture : IDisposable {
    private Image _image;
    private DeviceMemory _memory;
    private ImageView _imageView;
    private Sampler _sampler;
    private readonly bool _ownsResources;
    private bool _disposed;

    public uint Width => _image.Width;
    public uint Height => _image.Height;

    public ref readonly Image Image => ref _image;
    public ImageView ImageView => _imageView;
    public Sampler Sampler => _sampler;

    public SpriteTexture(Image image, DeviceMemory memory, ImageView imageView, Sampler sampler, bool ownsResources = true) {
        _image = image;
        _memory = memory;
        _imageView = imageView;
        _sampler = sampler;
        _ownsResources = ownsResources;
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        if (_ownsResources) {
            _sampler.Dispose();
            _imageView.Dispose();
            _memory.Dispose();
            _image.Dispose();
        }
        _disposed = true;
    }
}
