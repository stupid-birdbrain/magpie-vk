using System.Runtime.InteropServices;

namespace Auklet.Utilities;

internal static class UnsafeUtilities {
    internal static unsafe byte* ToPointer(this byte[] str) {
        return (byte*)Marshal.UnsafeAddrOfPinnedArrayElement(str, 0).ToPointer();
    }
    
    internal static unsafe byte* ToPointer(this string str) {
        return (byte*)Marshal.StringToHGlobalAnsi(str).ToPointer();
    }
    
    internal static unsafe string ToString(byte* str) {
        var stra = Marshal.PtrToStringAnsi((IntPtr)str);
        return stra!;
        
    }
    
    internal static unsafe byte** ToPointerArray(this string[] str) {
        var ptrs = new byte*[str.Length];
        for (int i = 0; i < str.Length; i++) {
            ptrs[i] = str[i].ToPointer();
        }
        fixed (byte** p = ptrs) {
            return p;
        }
    }
    
    internal static unsafe byte** ToPointerArray (this List<string> str) {
        // Cheap but whatever
        var array = str.ToArray();
        return array.ToPointerArray();
    }
}