using System;
using System.Collections;
using System.Collections.Generic;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class Display
    {
        readonly int index;

        internal Display(int index)
        {
            this.index = index;
        }

        public static int Count => ErrorIfNegative(SDL_GetNumVideoDisplays());

        public static Display Get(int index)
          => new Display(index);

        public static IEnumerable<Display> All()
        {
            int count = Count;
            for (int i = 0; i < count; ++i)
                yield return new Display(i);
        }

        public unsafe string Name
        {
            get
            {
                return ErrorIfNull(UTF8ToString(SDL_GetDisplayName(index)))!;
            }
        }

        public int Index => index;

        public Rect Bounds
        {
            get
            {
                Rect bounds;
                ErrorIfNegative(SDL_GetDisplayBounds(index, out bounds));
                return bounds;
            }
        }

        public Rect UsableBounds
        {
            get
            {
                Rect bounds;
                ErrorIfNegative(SDL_GetDisplayUsableBounds(index, out bounds));
                return bounds;
            }
        }

        public DisplayDPI DPI
        {
            get
            {
                float ddpi, hdpi, vdpi;
                ErrorIfNegative(SDL_GetDisplayDPI(index, out ddpi, out hdpi, out vdpi));
                return new DisplayDPI(ddpi, hdpi, vdpi);
            }
        }

        public DisplayModes Modes => new DisplayModes(index);


        public class DisplayModes : IReadOnlyList<DisplayMode>
        {
            readonly int index;
            internal DisplayModes(int index)
            {
                this.index = index;
            }

            public int Count => ErrorIfNegative(SDL_GetNumDisplayModes(index));

            public unsafe DisplayMode this[int modeIndex]
            {
                get
                {
                    SDL_DisplayMode mode;
                    ErrorIfNegative(SDL_GetDisplayMode(index, modeIndex, out mode));
                    return new DisplayMode(mode);
                }
            }

            public unsafe DisplayMode Current
            {
                get
                {
                    SDL_DisplayMode mode;
                    ErrorIfNegative(SDL_GetCurrentDisplayMode(index, out mode));
                    return new DisplayMode(mode);
                }
            }

            public unsafe DisplayMode Desktop
            {
                get
                {
                    SDL_DisplayMode mode;
                    ErrorIfNegative(SDL_GetDesktopDisplayMode(index, out mode));
                    return new DisplayMode(mode);
                }
            }

            public unsafe DisplayMode GetClosest(DisplayMode mode)
            {
                SDL_DisplayMode m;
                m.format = (uint)mode.Format;
                m.w = mode.Width;
                m.h = mode.Height;
                m.refresh_rate = mode.RefreshRate;
                m.driverdata = IntPtr.Zero;

                SDL_DisplayMode closest;
                ErrorIfNull((IntPtr)SDL_GetClosestDisplayMode(index, m, out closest));
                return new DisplayMode(closest);
            }

            IEnumerable<DisplayMode> Enumerate()
            {
                int c = Count;
                for (int i = 0; i < c; ++i)
                    yield return this[i];
            }

            public IEnumerator<DisplayMode> GetEnumerator()
            {
                return this.Enumerate().GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return this.GetEnumerator();
            }
        }
    }
}
