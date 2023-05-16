namespace Spice86.Aeon.Emulator.Video.Rendering; 

using System.Runtime.InteropServices;

/// <summary>
/// Represents a bitmap that is stored in unmanaged memory.
/// </summary>
public sealed class MemoryBitmap : IDisposable {
    private unsafe void* data;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryBitmap"/> class.
    /// </summary>
    /// <param name="width">The width of the bitmap, in pixels.</param>
    /// <param name="height">The height of the bitmap, in pixels.</param>
    public MemoryBitmap(int width, int height)
    {
        Width = width;
        Height = height;
        unsafe
        {
            data = NativeMemory.AlignedAlloc((nuint)(width * height * sizeof(uint)), sizeof(uint));
        }
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="MemoryBitmap"/> class.
    /// </summary>
    ~MemoryBitmap() => Dispose(false);

    /// <summary>
    /// Gets the width of the bitmap, in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height of the bitmap, in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets a pointer to the pixel buffer of the bitmap.
    /// </summary>
    public IntPtr PixelBuffer
    {
        get
        {
            unsafe
            {
                return new IntPtr(data);
            }
        }
    }


    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        unsafe
        {
            if (data != null)
            {
                NativeMemory.AlignedFree(data);
                data = null;
            }
        }
    }
}