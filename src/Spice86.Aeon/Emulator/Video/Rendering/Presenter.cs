namespace Spice86.Aeon.Emulator.Video.Rendering
{
    /// <summary>
    /// Renders emulated video RAM data to a bitmap.
    /// </summary>
    public abstract class Presenter : IDisposable
    {
        private Scaler? scaler;
        private MemoryBitmap? internalBuffer;
        private readonly object syncLock = new();
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="Presenter"/> class.
        /// </summary>
        /// <param name="videoMode"><see cref="Video.VideoMode"/> instance describing the video mode.</param>
        protected Presenter(VideoMode videoMode)
        {
            VideoMode = videoMode;
        }

        /// <summary>
        /// Gets or sets the scaler used on the output.
        /// </summary>
        public ScalingAlgorithm Scaler
        {
            get
            {
                return scaler switch
                {
                    Scale2x => ScalingAlgorithm.Scale2x,
                    Scale3x => ScalingAlgorithm.Scale3x,
                    _ => ScalingAlgorithm.None
                };
            }
            set
            {
                if (Scaler == value)
                    return;

                if (value != ScalingAlgorithm.None && internalBuffer == null)
                {
                    internalBuffer = new MemoryBitmap(VideoMode.PixelWidth, VideoMode.PixelHeight);
                }
                else
                {
                    internalBuffer?.Dispose();
                    internalBuffer = null;
                }

                scaler = value switch
                {
                    ScalingAlgorithm.Scale2x => new Scale2x(VideoMode.PixelWidth, VideoMode.PixelHeight),
                    ScalingAlgorithm.Scale3x => new Scale3x(VideoMode.PixelWidth, VideoMode.PixelHeight),
                    _ => null
                };
            }
        }
        /// <summary>
        /// Gets the required pixel width of the render target.
        /// </summary>
        public int TargetWidth => scaler?.TargetWidth ?? VideoMode.PixelWidth;
        /// <summary>
        /// Gets the required pixel height of the render target.
        /// </summary>
        public int TargetHeight {
            get => scaler?.TargetHeight ?? VideoMode.PixelHeight;
        }

        /// <summary>
        /// Gets the width ratio of the output if a scaler is being used; otherwise 1.
        /// </summary>
        public int WidthRatio => scaler?.WidthRatio ?? 1;
        /// <summary>
        /// Gets the height ratio of the output if a scaler is being used; otherwise 1.
        /// </summary>
        public int HeightRatio => scaler?.HeightRatio ?? 1;

        public TimeSpan RenderTime { get; private set; }
        public TimeSpan ScalerTime { get; private set; }

        /// <summary>
        /// Gets information about the video mode.
        /// </summary>
        protected VideoMode VideoMode { get; }

        /// <summary>
        /// Updates the bitmap to match the current state of the video RAM.
        /// </summary>
        public void Update(nint destination)
        {
            lock (syncLock)
            {
                if (scaler == null)
                {
                    DrawFrame(destination);
                }
                else
                {
                    DrawFrame(internalBuffer!.PixelBuffer);
                    scaler.Apply(internalBuffer.PixelBuffer, destination);
                }
            }
        }

        public virtual MemoryBitmap? Dump() => null;

        /// <summary>
        /// Updates the bitmap to match the current state of the video RAM.
        /// </summary>
        protected abstract void DrawFrame(IntPtr destination);

        public void Dispose()
        {
            lock (syncLock)
            {
                if (!disposed)
                {
                    internalBuffer?.Dispose();
                    disposed = true;
                    GC.SuppressFinalize(this);
                }
            }
        }
    }
}
