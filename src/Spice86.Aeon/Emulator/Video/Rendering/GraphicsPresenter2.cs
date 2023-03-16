namespace Spice86.Aeon.Emulator.Video.Rendering
{
    public sealed class GraphicsPresenter2 : Presenter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsPresenter2"/> class.
        /// </summary>
        /// <param name="dest">Pointer to destination bitmap.</param>
        /// <param name="videoMode">VideoMode instance describing the video mode.</param>
        public GraphicsPresenter2(VideoMode videoMode) : base(videoMode)
        {
        }

        protected override void DrawFrame(IntPtr destination)
        {
            int width = VideoMode.Width;
            int height = VideoMode.Height;
            int stride = VideoMode.Stride;

            unsafe
            {
                byte* srcPtr = (byte*)VideoMode.VideoRam.ToPointer();
                uint* destPtr = (uint*)destination.ToPointer();

                uint* palette = stackalloc uint[4];
                palette[0] = 0;
                palette[1] = 0x0000FFFF;
                palette[2] = 0x00FF00FF;
                palette[3] = 0x00FFFFFF;

                for (int y = 0; y < height; y += 2)
                {
                    byte* srcRow = srcPtr + (stride * (y / 2));
                    uint srcBit = 0;

                    for (int x = 0; x < width; x++)
                    {
                        uint srcByte = srcRow[srcBit / 8];
                        uint shift = 6 - (srcBit % 8);

                        uint c = Intrinsics.ExtractBits(srcByte, (byte)shift, 2, 0b11u << (int)shift);

                        destPtr[(y * width) + x] = palette[c];
                        srcBit += 2;
                    }
                }

                for (int y = 1; y < height; y += 2)
                {
                    byte* srcRow = 8192 + srcPtr + (stride * (y / 2));
                    uint srcBit = 0;

                    for (int x = 0; x < width; x++)
                    {
                        uint srcByte = srcRow[srcBit / 8];
                        uint shift = 6 - (srcBit % 8);

                        uint c = Intrinsics.ExtractBits(srcByte, (byte)shift, 2, 0b11u << (int)shift);

                        destPtr[(y * width) + x] = palette[c];
                        srcBit += 2;
                    }
                }
            }
        }
    }
}
