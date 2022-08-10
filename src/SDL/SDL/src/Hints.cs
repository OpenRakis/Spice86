using System;
using System.Collections;
using System.Collections.Generic;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;

namespace SDLSharp
{
    public class Hints
    {
        internal Hints() { }

        public Hint this[string name]
        {
            get
            {
                return new Hint(name);
            }
        }

        public void Clear()
        {
            SDL_ClearHints();
        }
    }

    public delegate void HintWatcher(string name, string? oldValue, string? newValue);

    public unsafe class Hint
    {
        readonly string name;
        Memory<byte> buf;

        internal Hint(string name)
        {
            this.name = name;
            buf = new Memory<byte>(new byte[SL(name)]);
            StringToUTF8(name, buf.Span);
        }

        public bool ToBool(bool defaultValue)
        {
            fixed (byte* nam = buf.Span)
                return SDL_GetHintBoolean(nam, defaultValue ? SDL_Bool.True : SDL_Bool.False) == SDL_Bool.True;
        }

        public override string? ToString()
        {
            fixed (byte* nam = buf.Span)
                return UTF8ToString(SDL_GetHint(nam));
        }

        public bool Set(string value)
        {
            Span<byte> b = stackalloc byte[SL(value)];
            StringToUTF8(value, b);
            fixed (byte* v = b)
            fixed (byte* n = buf.Span)
                return SDL_SetHint(n, v) == SDL_Bool.True;
        }

        public bool Set(string value, HintPriority priority)
        {
            Span<byte> b = stackalloc byte[SL(value)];
            StringToUTF8(value, b);
            fixed (byte* v = b)
            fixed (byte* n = buf.Span)
                return SDL_SetHintWithPriority(n, v, priority) == SDL_Bool.True;
        }

        public static implicit operator string?(Hint h)
        {
            return h.ToString();
        }


        private static List<WatchReg> watches = new List<WatchReg>();

        public event HintWatcher Watch
        {
            add
            {
                var wr = new WatchReg(value);
                fixed (byte* b = buf.Span)
                    SDL_AddHintCallback(b, wr.fp, IntPtr.Zero);
                lock (watches)
                    watches.Add(wr);
            }
            remove
            {
                WatchReg w;
                lock (watches)
                {
                    int i = watches.FindIndex(x => x.func == value);
                    if (i >= 0)
                    {
                        w = watches[i];
                        watches.RemoveAt(i);
                    }
                    else
                    {
                        return;
                    }
                }

                fixed (byte* b = buf.Span)
                    SDL_DelHintCallback(b, w.fp, IntPtr.Zero);
            }
        }

        struct WatchReg
        {
            public SDL_HintCallback del;
            public IntPtr fp;
            public HintWatcher func;

            public unsafe WatchReg(HintWatcher func)
            {
                this.func = func;
                del = (IntPtr ud, byte* name, byte* oldValue, byte* newValue) =>
                {
                    try
                    {
                        func(UTF8ToString(name) ?? "", UTF8ToString(oldValue), UTF8ToString(newValue));
                    }
                    catch (Exception e)
                    {
                        SDL.OnUnhandledException(e, true);
                    }
                };
                fp = Marshal.GetFunctionPointerForDelegate(del);
            }
        }
    }
}
