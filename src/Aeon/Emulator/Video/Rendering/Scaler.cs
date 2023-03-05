using System.Numerics;

namespace Aeon.Emulator.Video.Rendering
{
    internal abstract class Scaler
    {
        protected Scaler(int width, int height)
        {
            SourceWidth = width;
            SourceHeight = height;
        }

        public int SourceWidth { get; }
        public int SourceHeight { get; }
        public abstract int TargetWidth { get; }
        public abstract int TargetHeight { get; }
        public int WidthRatio => TargetWidth / SourceWidth;
        public int HeightRatio => TargetHeight / SourceHeight;

        public void Apply(IntPtr source, IntPtr destination)
        {
            if (Vector.IsHardwareAccelerated)
                VectorScale(source, destination);
            else
                Scale(source, destination);
        }

        protected abstract void Scale(IntPtr source, IntPtr destination);
        protected abstract void VectorScale(IntPtr source, IntPtr destination);
    }
}
