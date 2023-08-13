namespace Bufdio.Spice86.Utilities;

using System;
using System.Runtime.InteropServices;

internal sealed class LibraryLoader : IDisposable {
    private IntPtr _handle = IntPtr.Zero;
    private bool _disposed;

    public bool Initialize(string libraryName) {
        ArgumentException.ThrowIfNullOrEmpty(libraryName);
        if (!NativeLibrary.TryLoad(libraryName, out _handle)) {
            return false;
        }

        Ensure.That<Exception>(_handle != IntPtr.Zero, $"Could not load native library: {libraryName}.");
        return true;
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
