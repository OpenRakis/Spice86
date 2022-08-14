using System;
using System.Runtime.InteropServices;
using Bufdio.Interop;

namespace Bufdio.Utilities;

internal sealed class LibraryLoader : IDisposable
{
    private const int RTLD_NOW = 2;
    private readonly IntPtr _handle;
    private bool _disposed;

    public LibraryLoader(string libraryName)
    {
        Ensure.NotNull(libraryName, nameof(libraryName));

        if (PlatformInfo.IsWindows)
        {
            _handle = Kernel32.LoadLibrary(libraryName);
        }
        else if (!((PlatformInfo.IsLinux || PlatformInfo.IsOSX) && NativeLibrary.TryLoad(libraryName, out _handle))) {
            throw new NotSupportedException("Platform is not supported.");
            
        }

        Ensure.That<Exception>(_handle != IntPtr.Zero, $"Could not load native libary: {libraryName}.");
    }

    public TDelegate LoadFunc<TDelegate>(string name)
    {
        var ptr = PlatformInfo.IsWindows ? Kernel32.GetProcAddress(_handle, name) : NativeLibrary.GetExport(_handle, name);
        Ensure.That<Exception>(ptr != IntPtr.Zero, $"Could not load function name: {name}.");

        return Marshal.GetDelegateForFunctionPointer<TDelegate>(ptr);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (PlatformInfo.IsWindows)
        {
            Kernel32.FreeLibrary(_handle);
        }
        else
        {
            NativeLibrary.Free(_handle);
        }

        _disposed = true;
    }
}
