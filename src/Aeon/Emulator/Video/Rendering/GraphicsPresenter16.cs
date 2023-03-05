namespace Aeon.Emulator.Video.Rendering
{
    public sealed class GraphicsPresenter16 : Presenter
    {
        private const double RedRatio = 255.0 / 31.0;
        private const double GreenRatio = 255.0 / 63.0;
        private const double BlueRatio = 255.0 / 31.0;

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsPresenter16"/> class.
        /// </summary>
        /// <param name="videoMode">VideoMode instance describing the video mode.</param>
        public GraphicsPresenter16(VideoMode videoMode) : base(videoMode)
        {
        }

        /// <summary>
        /// Updates the bitmap to match the current state of the video RAM.
        /// </summary>
        protected override void DrawFrame(IntPtr destination)
        {
            int totalPixels = VideoMode.Width * VideoMode.Height;

            unsafe
            {
                ushort* srcPtr = (ushort*)((byte*)VideoMode.VideoRam.ToPointer() + VideoMode.StartOffset);
                uint* destPtr = (uint*)destination.ToPointer();

                for (int i = 0; i < totalPixels; i++)
                    destPtr[i] = Make32Bit(srcPtr[i]);
            }
        }

        private static uint Make32Bit(uint src)
        {
            uint r = (uint)(((src & 0xF800) >> 11) * RedRatio) & 0xFFu;
            uint g = (uint)(((src & 0x07E0) >> 5) * GreenRatio) & 0xFFu;
            uint b = (uint)((src & 0x001F) * BlueRatio) & 0xFFu;

            return (r << 16) | (g << 8) | b;
        }
    }
}
