namespace Magpie.Utilities;

public static class SpanUtilities {
    public unsafe static T* GetPointer<T>(this ReadOnlySpan<T> span) {
        fixed(T* ptr = span) {
            return ptr;
        }
    }
    
    public unsafe static T* GetPointer<T>(this Span<T> span) {
        fixed(T* ptr = span) {
            return ptr;
        }
    }
}