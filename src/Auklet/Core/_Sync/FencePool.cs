using Auklet.Core;
using Vortice.Vulkan;

namespace Auklet.Core;

public sealed class FencePool {
    private readonly LogicalDevice _device;
    private readonly Stack<Fence> _pool = new();
    private bool _disposed;

    public FencePool(LogicalDevice device) {
        _device = device;
    }

    public FenceLease Rent(VkFenceCreateFlags flags) {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(FencePool));
        }

        if (_pool.TryPop(out var fence)) {
            fence.Reset();
            return new FenceLease(fence, this);
        }

        var newFence = new Fence(_device, flags);
        return new(newFence, this);
    }

    internal void Return(FenceLease lease) {
        if (!_disposed) {
            _pool.Push(lease.Value);
        }
        else {
            var fence = lease.Value;
            fence.Dispose();
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }
        
        while (_pool.TryPop(out var fence)) {
            fence.Dispose();
        }

        _disposed = true;
    }
}

public readonly struct FenceLease : IDisposable {
    public readonly Fence Value { get; }
    private readonly FencePool _pool;

    internal FenceLease(Fence fence, FencePool pool) {
        Value = fence;
        _pool = pool;
    }

    public void Dispose() {
        _pool.Return(this);
    }
    
    public void Wait() => Value.Wait();

    public static implicit operator Fence(FenceLease lease) => lease.Value;
    public static implicit operator VkFence(FenceLease lease) => lease.Value;
}