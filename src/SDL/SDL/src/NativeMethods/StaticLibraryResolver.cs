using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SDLSharp; 

static unsafe partial class NativeMethods {
    internal const string LibSDL2Name = "SDL2";
    static NativeMethods() =>
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), DllImportResolver);
    
    static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        IntPtr handle;
        if (NativeLibrary.TryLoad(LibSDL2Name, assembly, searchPath, out handle)) {
            return handle;
        }
        return IntPtr.Zero;
    }
}