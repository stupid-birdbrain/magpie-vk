namespace Magpie.Utilities;

internal static class SpanUtilities {
    internal static unsafe T* GetPointer<T>(this ReadOnlySpan<T> span) where T : unmanaged {
        fixed(T* ptr = span) {
            return ptr;
        }
    }
}