
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class DisplayDPI
    {
        public float Diagonal { get; }
        public float Horizontal { get; }
        public float Vertical { get; }

        public DisplayDPI(float diagonal, float horizontal, float vertical)
        {
            this.Diagonal = diagonal;
            this.Horizontal = horizontal;
            this.Vertical = vertical;
        }

        public override string ToString()
        {
            return $"{{Diagonal={Diagonal},Horizontal={Horizontal},Vertical={Vertical}}}";
        }
    }
}
