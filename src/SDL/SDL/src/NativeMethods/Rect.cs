using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_EnclosePoints(
          /*const*/ Point* points,
          int count,
          in Rect clip,
          out Rect result
        );

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_EnclosePoints(
          /*const*/ Point* points,
          int count,
          IntPtr clip,
          out Rect result
        );

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_HasIntersection(
          in Rect a,
          in Rect b
        );

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_IntersectRect(
          in Rect a,
          in Rect b,
          out Rect result
        );

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_IntersectRectAndLine(
          in Rect rect,
          ref int x1,
          ref int y1,
          ref int x2,
          ref int y2
        );

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_PointInRect(
          in Point p,
          in Rect r
        );

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_RectEmpty(
          in Rect r
        );

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_RectEquals(
          in Rect a,
          in Rect b
        );

        [DllImport("SDL2")]
        public static extern void SDL_UnionRect(
          in Rect a,
          in Rect b,
          out Rect result
        );
    }

    [StructLayout(LayoutKind.Sequential)]
    public partial struct Point
    {
        public int x, y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public partial struct Rect
    {
        public int x, y, w, h;
    }
}
