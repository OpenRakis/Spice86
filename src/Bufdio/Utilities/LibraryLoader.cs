using System;
using System.Runtime.InteropServices;

namespace Bufdio.Utilities;

internal sealed class LibraryLoader : IDisposable
{
    private readonly IntPtr _handle;
    private bool _disposed;

    public LibraryLoader(string libraryName)
    {
        Ensure.NotNull(libraryName, nameof(libraryName));
        
        if (!NativeLibrary.TryLoad(libraryName, out _handle)) {
            throw new NotSupportedException("Platform is not supported.");
            
        }

        Ensure.That<Exception>(_handle != IntPtr.Zero, $"Could not load native libary: {libraryName}.");
    }

    public TDelegate LoadFunc<TDelegate>(string name)
    {
        IntPtr ptr = NativeLibrary.GetExport(_handle, name);
        Ensure.That<Exception>(ptr != IntPtr.Zero, $"Could not load function name: {name}.");

        return Marshal.GetDelegateForFunctionPointer<TDelegate>(ptr);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        NativeLibrary.Free(_handle);
        _disposed = true;
    }
}
