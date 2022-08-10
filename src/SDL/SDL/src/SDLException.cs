using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public class SDLException : Exception
    {
        public SDLException(string message) : base(message)
        {
        }
    }
}
