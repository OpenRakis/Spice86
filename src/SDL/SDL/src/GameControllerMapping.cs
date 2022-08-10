using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public unsafe class GameControllerMapping : SafeHandle
    {
        protected GameControllerMapping() : base(IntPtr.Zero, true)
        {
        }

        public GameControllerMapping(IntPtr h, bool owned) : base(IntPtr.Zero, owned)
        {
            SetHandle(h);
        }

        public static GameControllerMapping ForGuid(Guid g)
        {
            SDL_JoystickGUID guid;
            var s = new Span<byte>(guid.data, 16);
            bool wrote = g.TryWriteBytes(s);
            System.Diagnostics.Debug.Assert(wrote);
            return ErrorIfInvalid(SDL_GameControllerMappingForGUID(guid));
        }

        public override string ToString()
        {
            if (IsInvalid) throw new ObjectDisposedException(nameof(GameControllerMapping));
            return UTF8ToString((byte*)handle) ?? "";
        }

        public static implicit operator string(GameControllerMapping m)
          => m.ToString();

        public override bool IsInvalid => handle == IntPtr.Zero;

        override protected bool ReleaseHandle()
        {
            NativeMethods.SDL_free((void*)this.handle);
            return true;
        }
    }
}
