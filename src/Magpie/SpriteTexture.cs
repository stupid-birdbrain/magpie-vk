using System;
using Magpie.Core;

namespace Magpie;

public sealed class SpriteTexture : IDisposable {
    private Image _image;
    private DeviceMemory _memory;
    private ImageView _imageView;
    private Sampler _sampler;
    private readonly bool _ownsImage;
    private readonly bool _ownsMemory;
    private readonly bool _ownsImageView;
    private readonly bool _ownsSampler;
    private bool _disposed;

    public uint Width => _image.Width;
    public uint Height => _image.Height;

    public ref readonly Image Image => ref _image;
    public ImageView ImageView => _imageView;
    public Sampler Sampler => _sampler;

    public SpriteTexture(
        Image image,
        DeviceMemory memory,
        ImageView imageView,
        Sampler sampler,
        bool ownsImage = true,
        bool ownsMemory = true,
        bool ownsImageView = true,
        bool ownsSampler = true) {
        _image = image;
        _memory = memory;
        _imageView = imageView;
        _sampler = sampler;
        _ownsImage = ownsImage;
        _ownsMemory = ownsMemory;
        _ownsImageView = ownsImageView;
        _ownsSampler = ownsSampler;
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        if (_ownsSampler) {
            _sampler.Dispose();
        }
        if (_ownsImageView) {
            _imageView.Dispose();
        }
        if (_ownsMemory) {
            _memory.Dispose();
        }
        if (_ownsImage) {
            _image.Dispose();
        }
        _disposed = true;
    }
}
