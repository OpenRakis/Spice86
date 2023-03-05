using System.Buffers.Binary;

namespace Aeon.Emulator.Video.Rendering
{
    

    /// <summary>
    /// Renders 8-bit mode X graphics to a bitmap.
    /// </summary>
    public sealed class GraphicsPresenterX : Presenter
    {
        /// <summary>
        /// Initializes a new instance of the GraphicsPresenterX class.
        /// </summary>
        /// <param name="dest">Pointer to destination bitmap.</param>
        /// <param name="videoMode">VideoMode instance describing the video mode.</param>
        public GraphicsPresenterX(VideoMode videoMode) : base(videoMode)
        {
        }



        public override MemoryBitmap Dump()
        {
            int width = VideoMode.Width;
            int height = VideoMode.Height;
            var palette = VideoMode.Palette;
            int startOffset = VideoMode.StartOffset;
            int stride = VideoMode.Stride;
            int lineCompare = VideoMode.LineCompare / 2;

            unsafe
            {
                var bmp = new MemoryBitmap(stride * 4, IVgaCard.TotalVramBytes / (stride * 4));
                uint* destPtr = (uint*)bmp.PixelBuffer.ToPointer();
                uint* src = (uint*)VideoMode.VideoRam.ToPointer();

                int max = Math.Min(height, lineCompare + 1);
                int wordWidth = stride;

                Span<byte> byteBuf = stackalloc byte[4];

                for (int y = 0; y < bmp.Height; y++)
                {
                    int srcPos = y * stride;
                    int destPos = y * bmp.Width;

                    for (int x = 0; x < wordWidth; x++)
                    {
                        uint p = src[(srcPos + x) & ushort.MaxValue];
                        BinaryPrimitives.WriteUInt32LittleEndian(byteBuf, p);
                        destPtr[destPos++] = palette[byteBuf[0]];
                        destPtr[destPos++] = palette[byteBuf[1]];
                        destPtr[destPos++] = palette[byteBuf[2]];
                        destPtr[destPos++] = palette[byteBuf[3]];
                    }
                }

                return bmp;
            }
        }

        /// <summary>
        /// Updates the bitmap to match the current state of the video RAM.
        /// </summary>
        protected override void DrawFrame(IntPtr destination)
        {
            int width = VideoMode.Width;
            int height = VideoMode.Height;
            var palette = VideoMode.Palette;
            int startOffset = VideoMode.StartOffset;
            int stride = VideoMode.Stride;
            int lineCompare = VideoMode.LineCompare / 2;

            unsafe
            {
                uint* destPtr = (uint*)destination.ToPointer();
                uint* src = (uint*)VideoMode.VideoRam.ToPointer();
                
                int max = Math.Min(height, lineCompare + 1);
                int wordWidth = width / 4;

                Span<byte> byteBuf = stackalloc byte[4];

                for (int y = 0; y < max; y++)
                {
                    int srcPos = (y * stride) + startOffset;
                    int destPos = y * width;

                    for (int x = 0; x < wordWidth; x++)
                    {
                        uint p = src[(srcPos + x) & ushort.MaxValue];
                        BinaryPrimitives.WriteUInt32LittleEndian(byteBuf, p);
                        destPtr[destPos++] = palette[byteBuf[0]];
                        destPtr[destPos++] = palette[byteBuf[1]];
                        destPtr[destPos++] = palette[byteBuf[2]];
                        destPtr[destPos++] = palette[byteBuf[3]];
                    }
                }

                if (max < height)
                {
                    for (int y = max + 1; y < height; y++)
                    {
                        int srcPos = (y - max) * stride;
                        int destPos = y * width;

                        for (int x = 0; x < wordWidth; x++)
                        {
                            uint p = src[(srcPos + x) & ushort.MaxValue];
                            BinaryPrimitives.WriteUInt32LittleEndian(byteBuf, p);
                            destPtr[destPos++] = palette[byteBuf[0]];
                            destPtr[destPos++] = palette[byteBuf[1]];
                            destPtr[destPos++] = palette[byteBuf[2]];
                            destPtr[destPos++] = palette[byteBuf[3]];
                        }
                    }
                }
            }
        }
    }
}
