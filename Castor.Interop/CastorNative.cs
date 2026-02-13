using System;
using System.Runtime.InteropServices;

namespace Castor.Interop
{
    internal static class CastorNative
    {
        private const string DllName = "castor_core";
        private const CallingConvention Convention = CallingConvention.Cdecl;

        [DllImport(DllName, CallingConvention = Convention)]
        internal static extern string get_verison();
    }
}
