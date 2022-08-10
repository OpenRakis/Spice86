using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {

        [DllImport("SDL2")]
        public static extern RWOps SDL_RWFromFile(
            /*const char*/ byte* file,
            /*const char*/ byte* mode);

        [DllImport("SDL2")]
        public static extern RWOps SDL_RWFromFP(IntPtr fp, SDL_Bool autoclose);

        [DllImport("SDL2")]
        public static extern RWOpsFromMemory SDL_RWFromMem(IntPtr mem, int size);

        [DllImport("SDL2")]
        public static extern RWOpsFromMemory SDL_RWFromConstMem(IntPtr mem, int size);

        [DllImport("SDL2")]
        public static extern RWOpsFromInterface SDL_AllocRW();

        [DllImport("SDL2")]
        public static extern void SDL_FreeRW(IntPtr area);


        [DllImport("SDL2")]
        public static extern byte SDL_ReadU8(RWOps src);

        [DllImport("SDL2")]
        public static extern ushort SDL_ReadLE16(RWOps src);

        [DllImport("SDL2")]
        public static extern ushort SDL_ReadBE16(RWOps src);

        [DllImport("SDL2")]
        public static extern uint SDL_ReadLE32(RWOps src);

        [DllImport("SDL2")]
        public static extern ulong SDL_ReadBE64(RWOps src);

        [DllImport("SDL2")]
        public static extern ulong SDL_ReadLE64(RWOps src);

        [DllImport("SDL2")]
        public static extern UIntPtr SDL_WriteU8(RWOps src, byte value);

        [DllImport("SDL2")]
        public static extern UIntPtr SDL_WriteLE16(RWOps src, ushort value);

        [DllImport("SDL2")]
        public static extern UIntPtr SDL_WriteBE16(RWOps src, ushort value);

        [DllImport("SDL2")]
        public static extern UIntPtr SDL_WriteLE32(RWOps src, uint value);

        [DllImport("SDL2")]
        public static extern UIntPtr SDL_WriteBE64(RWOps src, ulong value);

        [DllImport("SDL2")]
        public static extern UIntPtr SDL_WriteLE64(RWOps src, ulong value);


        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_RWops
        {
            public IntPtr size;
            public IntPtr seek;
            public IntPtr read;
            public IntPtr write;
            public IntPtr close;
            public RWOpsType type;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate long SDL_RWopsSize(SDL_RWops* io);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate long SDL_RWopsSeek(SDL_RWops* io, long offset, int whence);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate UIntPtr SDL_RWopsRead(SDL_RWops* io, byte* ptr, UIntPtr size, UIntPtr maxnum);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate UIntPtr SDL_RWopsWrite(SDL_RWops* io, /*const*/ byte* ptr, UIntPtr size, UIntPtr num);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SDL_RWopsClose(SDL_RWops* io);
    }

    public enum RWOpsType : uint
    {
        Unknown,
        Win32File,
        StdioFile,
        JNIFile,
        Memory,
        ReadOnlyMemory,
    }
}
