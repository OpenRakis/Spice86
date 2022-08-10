using System;
using System.Collections.Generic;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public static class Events
    {
        public static EventQueue Current { get; } = new EventQueue();
        public static IgnoredEvents Ignore { get; } = new IgnoredEvents();

        public class IgnoredEvents
        {
            public bool this[EventType type]
            {
                get
                {
                    return SDL_EventState((uint)type, -1) == 0;
                }
                set
                {
                    SDL_EventState((uint)type, value ? 0 : 1);
                }
            }
        }

        public static uint Register(int count)
        {
            return SDL_RegisterEvents(count);
        }

        struct WatchReg
        {
            public SDL_EventFilter del;
            public IntPtr fp;
            public EventWatcher func;

            public WatchReg(EventWatcher func)
            {
                this.func = func;
                del = (IntPtr ud, ref Event v) =>
                {
                    try
                    {
                        func(ref v);
                    }
                    catch (Exception e)
                    {
                        SDL.OnUnhandledException(e, true);
                    }
                    return 1;
                };
                fp = Marshal.GetFunctionPointerForDelegate(del);
            }
        }
        private static List<WatchReg> watches = new List<WatchReg>();

        public static event EventWatcher Watch
        {
            add
            {
                var wr = new WatchReg(value);
                SDL_AddEventWatch(wr.fp, IntPtr.Zero);
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

                SDL_DelEventWatch(w.fp, IntPtr.Zero);
            }
        }

        static SDL_EventFilter? currentfilter;
        public static EventFilter? Filter
        {
            get
            {
                IntPtr filter, userdata;
                if (SDL_GetEventFilter(out filter, out userdata) == SDL_Bool.True)
                {
                    SDL_EventFilter? f = Marshal.GetDelegateForFunctionPointer<SDL_EventFilter>(filter);
                    return (ref Event ev) =>
                    {
                        return f(userdata, ref ev) != 0;
                    };
                }
                else
                {
                    return null;
                }
            }
            set
            {
                if (value != null)
                {
                    SDL_EventFilter f = (IntPtr ud, ref Event ev) =>
                    {
                        try
                        {
                            return value(ref ev) ? 1 : 0;
                        }
                        catch (Exception e)
                        {
                            SDL.OnUnhandledException(e, true);
                            return 1;
                        }
                    };
                    SDL_SetEventFilter(Marshal.GetFunctionPointerForDelegate(f), IntPtr.Zero);
                    currentfilter = f;
                }
                else
                {
                    currentfilter = null;
                    SDL_SetEventFilter(IntPtr.Zero, IntPtr.Zero);
                }
            }
        }
    }

    public delegate void EventWatcher(ref Event ev);
    public delegate bool EventFilter(ref Event ev);
}
