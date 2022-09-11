using System;
using System.Runtime.InteropServices;

namespace Bufdio.Spice86.Utilities;

internal sealed class LibraryLoader : IDisposable {
    private IntPtr _handle = IntPtr.Zero;
    private bool _disposed;

    public LibraryLoader() {

    }

    public bool Initialize(string libraryName) {
        Ensure.NotNull(libraryName, nameof(libraryName));
        if (!NativeLibrary.TryLoad(libraryName, out _handle)) {
            return false;
        }

        Ensure.That<Exception>(_handle != IntPtr.Zero, $"Could not load native libary: {libraryName}.");
        return true;
    }

    public TDelegate LoadFunc<TDelegate>(string name) {
        IntPtr ptr = NativeLibrary.GetExport(_handle, name);
        Ensure.That<Exception>(ptr != IntPtr.Zero, $"Could not load function name: {name}.");

        return Marshal.GetDelegateForFunctionPointer<TDelegate>(ptr);
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (_disposed) {
            if (disposing && _handle != IntPtr.Zero) {
                NativeLibrary.Free(_handle);
            }
            _disposed = true;
        }
    }
}
