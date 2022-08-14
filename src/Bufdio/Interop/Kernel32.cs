using System;
using System.Runtime.InteropServices;

namespace Bufdio.Interop;

internal static class Kernel32
{
    private const string LibraryName = "kernel32";

    [DllImport(LibraryName)]
    public static extern IntPtr LoadLibrary(string fileName);

    [DllImport(LibraryName)]
    public static extern IntPtr GetProcAddress(IntPtr module, string procName);

    [DllImport(LibraryName)]
    public static extern int FreeLibrary(IntPtr module);
}
