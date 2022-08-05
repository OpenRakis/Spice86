namespace Spice86.WPF.CustomControls;
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

/// <summary>
/// Simplifies creation and management of an InteropBitmap.
/// </summary>
internal sealed class FastBitmap : IDisposable {
    private IntPtr section;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the FastBitmap class.
    /// </summary>
    /// <param name="width">Width of the bitmap in pixels.</param>
    /// <param name="height">Height of the bitmap in pixels.</param>
    public FastBitmap(int width, int height) {
        this.CreateInteropBitmap(width, height);
    }
    ~FastBitmap() {
        this.Dispose(false);
    }

    /// <summary>
    /// Gets the InteropBitmap instance.
    /// </summary>
    public InteropBitmap? InteropBitmap { get; private set; }
    /// <summary>
    /// Gets the pointer to the bitmap pixel data.
    /// </summary>
    public IntPtr PixelBuffer { get; private set; }

    /// <summary>
    /// Releases unmanaged resources used by the bitmap.
    /// </summary>
    public void Dispose() {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Forces a redraw of the InteropBitmap.
    /// </summary>
    public void Invalidate() => this.InteropBitmap?.Invalidate();

    /// <summary>
    /// Releases unmanaged resources used by the bitmp.
    /// </summary>
    /// <param name="disposing">Value indicating whether the method is called from the Dispose method.</param>
    private void Dispose(bool disposing) {
        if (!this.disposed) {
            if (this.PixelBuffer != IntPtr.Zero) {
                _ = UnmapViewOfFile(this.PixelBuffer);
                this.PixelBuffer = IntPtr.Zero;
            }

            if (this.section != IntPtr.Zero) {
                _ = CloseHandle(this.section);
                this.section = IntPtr.Zero;
            }

            this.disposed = true;
        }
    }
    /// <summary>
    /// Allocate the memory required by the bitmap.
    /// </summary>
    /// <param name="pixelWidth">Width of the bitmap in pixels.</param>
    /// <param name="pixelHeight">Height of the bitmap in pixels.</param>
    private void CreateInteropBitmap(int pixelWidth, int pixelHeight) {
        int byteSize = pixelWidth * pixelHeight * 4;
        this.section = CreateFileMapping(INVALID_HANDLE_VALUE, IntPtr.Zero, PAGE_READWRITE, 0, (uint)byteSize + 4096u, null);
        this.PixelBuffer = MapViewOfFile(section, FILE_MAP_ALL_ACCESS, 0, 0, (uint)byteSize);
        this.InteropBitmap = (InteropBitmap)Imaging.CreateBitmapSourceFromMemorySection(section, pixelWidth, pixelHeight, PixelFormats.Bgr32, pixelWidth * 4, 0);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileMapping(IntPtr hFile, IntPtr lpFileMappingAttributes, uint flProtect, uint dwMaximumSizeHigh, uint dwMaximumSizeLow, string? lpName);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, uint dwNumberOfBytesToMap);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint UnmapViewOfFile(IntPtr lpBaseAddress);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint CloseHandle(IntPtr hObject);

    private const uint FILE_MAP_ALL_ACCESS = 0xF001F;
    private const uint PAGE_READWRITE = 0x04;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);
}
