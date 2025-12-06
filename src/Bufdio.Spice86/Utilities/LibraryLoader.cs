namespace Bufdio.Spice86.Utilities;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Provides functionality for loading native libraries.
/// </summary>
internal sealed class LibraryLoader : IDisposable {
    private readonly IntPtr _handle;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryLoader"/> class and loads the specified native library.
    /// </summary>
    /// <param name="libraryName">The name of the native library to load.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="libraryName"/> is null or empty.</exception>
    /// <exception cref="Exception">Thrown when the library could not be loaded.</exception>
    public LibraryLoader(string libraryName) {
        ArgumentException.ThrowIfNullOrEmpty(libraryName);
        _handle = NativeLibrary.Load(libraryName);

        Ensure.That<Exception>(_handle != IntPtr.Zero, $"Could not load native library: {libraryName}.");
    }

    /// <summary>
    /// Releases all resources used by the <see cref="LibraryLoader"/>.
    /// </summary>
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