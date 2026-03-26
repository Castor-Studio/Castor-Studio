using System.Runtime.InteropServices;

namespace Castor.Native
{
    public static class CastorNative
    {
        private const string DllName = "libcastor_core";
        private const CallingConvention Convention = CallingConvention.Cdecl;

        [DllImport(DllName, CallingConvention = Convention)]
        public static extern IntPtr get_version();

        public static string GetVersion()
        {
            IntPtr versionPtr = get_version();
            string? version = Marshal.PtrToStringAnsi(versionPtr);
            return version ?? "Unknown version";
        }
    }
}
