using System;
using System.Drawing;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public partial struct Point
    {
        public Point(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public void Deconstruct(out int x, out int y)
        {
            x = this.x;
            y = this.y;
        }

        public static implicit operator System.Drawing.Point(in Point p)
        {
            return new System.Drawing.Point(p.x, p.y);
        }

        public static implicit operator Point(in System.Drawing.Point p)
        {
            return new Point(p.X, p.Y);
        }
    }

    public partial struct Rect
    {

        public Rect(int x, int y, int w, int h)
        {
            this.x = x;
            this.y = y;
            this.w = w;
            this.h = h;
        }

        public Rect(in Point pt, in Size sz)
        {
            this.x = pt.x;
            this.y = pt.y;
            this.w = sz.Width;
            this.h = sz.Height;
        }

        public void Deconstruct(out int x, out int y, out int w, out int h)
        {
            x = this.x;
            y = this.y;
            w = this.w;
            h = this.h;
        }

        public static bool TryIntersect(in Rect a, in Rect b, out Rect result)
        {
            return SDL_IntersectRect(a, b, out result) == SDL_Bool.True;
        }

        public static bool Contains(in Point pt, in Rect rect)
        {
            return SDL_PointInRect(pt, rect) == SDL_Bool.True;
        }

        public static Rect Union(in Rect a, in Rect b)
        {
            Rect result;
            SDL_UnionRect(a, b, out result);
            return result;
        }

        public static void Union(in Rect a, in Rect b, out Rect result)
        {
            SDL_UnionRect(a, b, out result);
        }

        public static bool IsEmpty(in Rect a)
        {
            return SDL_RectEmpty(a) == SDL_Bool.True;
        }

        public static bool Equals(in Rect a, in Rect b)
        {
            return SDL_RectEquals(a, b) == SDL_Bool.True;
        }

        public static bool HasIntersection(in Rect a, in Rect b)
        {
            return SDL_HasIntersection(a, b) == SDL_Bool.True;
        }

        public static bool IntersectLine(in Rect rect, ref int x1, ref int y1, ref int x2, ref int y2)
        {
            return SDL_IntersectRectAndLine(rect, ref x1, ref y1, ref x2, ref y2) == SDL_Bool.True;
        }

        public static bool IntersectLine(in Rect rect, ref Point a, ref Point b)
        {
            return SDL_IntersectRectAndLine(
                rect,
                ref a.x,
                ref a.y,
                ref b.x,
                ref b.y
            ) == SDL_Bool.True;
        }

        public static unsafe Rect EnclosePoints(ReadOnlySpan<Point> points)
        {
            Rect result;
            fixed (Point* pointptr = &MemoryMarshal.GetReference(points))
                SDL_EnclosePoints(pointptr, points.Length, IntPtr.Zero, out result);
            return result;
        }

        public static unsafe Rect EnclosePoints(ReadOnlySpan<Point> points, out bool enclosed)
        {
            Rect result;
            fixed (Point* pointptr = &MemoryMarshal.GetReference(points))
                enclosed = SDL_EnclosePoints(pointptr, points.Length, IntPtr.Zero, out result) == SDL_Bool.True;
            return result;
        }

        public static unsafe Rect EnclosePoints(ReadOnlySpan<Point> points, in Rect clip)
        {
            Rect result;
            fixed (Point* pointptr = &MemoryMarshal.GetReference(points))
                SDL_EnclosePoints(pointptr, points.Length, clip, out result);
            return result;
        }

        public static unsafe Rect EnclosePoints(ReadOnlySpan<Point> points, in Rect clip, out bool enclosed)
        {
            Rect result;
            fixed (Point* pointptr = &MemoryMarshal.GetReference(points))
                enclosed = SDL_EnclosePoints(pointptr, points.Length, clip, out result) == SDL_Bool.True;
            return result;
        }

        public static implicit operator Rectangle(in Rect r)
        {
            return new Rectangle(r.x, r.y, r.w, r.h);
        }

        public static implicit operator Rect(in Rectangle b)
        {
            return new Rect(b.X, b.Y, b.Width, b.Height);
        }

        public override string ToString()
        {
            Rectangle r = this;
            return r.ToString();
        }
    }
}
