using System;
using System.Text;
using System.Drawing;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public class BorderSize
    {
        public int Top { get; }
        public int Left { get; }
        public int Bottom { get; }
        public int Right { get; }

        public BorderSize(int top, int left, int bottom, int right)
        {
            Top = top;
            Left = left;
            Bottom = bottom;
            Right = right;
        }

        public override string ToString()
        {
            return $"{{Top={Top},Left={Left},Right={Right},Bottom={Bottom}}}";
        }
    }
}
