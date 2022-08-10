using System;
using System.Runtime.InteropServices;
using System.Threading;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public static class Timer
    {
        public static uint Ticks => SDL_GetTicks();
        public static ulong PerformanceCounter => SDL_GetPerformanceCounter();
        public static ulong PerformanceFrequency => SDL_GetPerformanceFrequency();

        public static TimerRegistration AddTimer(uint interval, Func<uint, uint> callback)
        {
            SDL_TimerCallback cb = (uint iv, IntPtr param) =>
            {
                try
                {
                    return callback(iv);
                }
                catch (Exception e)
                {
                    SDL.OnUnhandledException(e, true);
                    return 0;
                }
            };
            int id = ErrorIfZero(SDL_AddTimer(interval, Marshal.GetFunctionPointerForDelegate(cb), IntPtr.Zero));
            return new TimerRegistration(id, cb);
        }

        public struct TimerRegistration : IDisposable
        {
            readonly int id;
            SDL_TimerCallback cb;
            int disposed;

            internal TimerRegistration(int id, SDL_TimerCallback cb)
            {
                this.id = id;
                this.cb = cb;
                this.disposed = 0;
            }

            public override string ToString()
            {
                return $"{nameof(TimerRegistration)}{id}";
            }

            public void Dispose()
            {
                int d = Interlocked.Exchange(ref disposed, 1);
                if (d == 0)
                {
                    SDL_RemoveTimer(id);
                }
            }
        }
    }
}
