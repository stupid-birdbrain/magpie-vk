namespace Magpie.Utilities;

public static class SpanUtilities {
    public static unsafe T* GetPointer<T>(this ReadOnlySpan<T> span) where T : unmanaged {
        fixed(T* ptr = span) {
            return ptr;
        }
    }
    
    public static unsafe T* GetPointer<T>(this Span<T> span) where T : unmanaged {
        fixed(T* ptr = span) {
            return ptr;
        }
    }
}