using System.Runtime.InteropServices;
using System.Text;

namespace Magpie.Utilities;

public static class StringExtensions {
    public static unsafe byte* ToUtf8CharPtr(this string str) {
        if (str == null) {
            return null;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(str);
        IntPtr ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        ((byte*)ptr)[bytes.Length] = 0;

        return (byte*)ptr;
    }
}