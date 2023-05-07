using System.Runtime.InteropServices;

namespace Spice86.Aeon.Emulator.Video.Rendering; 

public sealed class MemoryBitmap : IDisposable
{
    private unsafe void* data;

    public MemoryBitmap(int width, int height)
    {
        Width = width;
        Height = height;
        unsafe
        {
            data = NativeMemory.AlignedAlloc((nuint)(width * height * sizeof(uint)), sizeof(uint));
        }
    }
    ~MemoryBitmap() => Dispose(false);

    public int Width { get; }
    public int Height { get; }
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