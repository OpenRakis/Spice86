using System;
using System.Text;
using System.Drawing;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public class Cursor : SafeHandle
    {

        private Cursor() : base(IntPtr.Zero, true)
        {
        }

        internal Cursor(IntPtr ptr, bool owned) : base(IntPtr.Zero, owned)
        {
            SetHandle(ptr);
        }

        public Cursor(SystemCursor id) : this()
        {
            Cursor? cursor = ErrorIfInvalid(SDL_CreateSystemCursor(id));
            SetHandle(cursor.handle);
            cursor.SetHandle(IntPtr.Zero);
        }

        public Cursor(Surface surface, int hotX, int hotY) : this()
        {
            Cursor? cursor = ErrorIfInvalid(SDL_CreateColorCursor(surface, hotX, hotY));
            SetHandle(cursor.handle);
            cursor.SetHandle(IntPtr.Zero);
        }

        public static Cursor? Current
        {
            get
            {
                IntPtr ptr = SDL_GetCursor();
                if (ptr == IntPtr.Zero)
                    return null;
                return new Cursor(ptr, false);
            }
            set
            {
                if (value == null)
                    SDL_SetCursor(IntPtr.Zero);
                else
                    SDL_SetCursor(value);
            }
        }

        public static Cursor Default
        {
            get
            {
                return new Cursor(ErrorIfNull(SDL_GetDefaultCursor()), false);
            }
        }

        public static bool Visible
        {
            get
            {
                return ErrorIfNegative(SDL_ShowCursor(-1)) == 1;
            }
            set
            {
                ErrorIfNegative(SDL_ShowCursor(value ? 1 : 0));
            }
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        override protected bool ReleaseHandle()
        {
            NativeMethods.SDL_FreeCursor(this.handle);
            return true;
        }
    }
}
