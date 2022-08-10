using System;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public static class Video
    {
        public static IEnumerable<Display> Displays => Display.All();
        public static VideoDrivers Drivers { get; } = new VideoDrivers();

        public static unsafe void Init(string? driverName = null)
        {
            bool disableDrop = SDL.ShouldDisableDropAfterInit(InitFlags.Video);
            if (driverName != null)
            {
                Span<byte> buf = stackalloc byte[SL(driverName)];
                StringToUTF8(driverName, buf);
                fixed (byte* ptr = &MemoryMarshal.GetReference(buf))
                    ErrorIfNegative(SDL_VideoInit(ptr));
            }
            else
            {
                ErrorIfNegative(SDL_VideoInit(null));
            }
            if (disableDrop) SDL.DisableDropEvents();
        }

        public static void Quit()
        {
            SDL_VideoQuit();
        }

        public static bool ScreensaverEnabled
        {
            get { return SDL_IsScreenSaverEnabled() == SDL_Bool.True; }
            set
            {
                if (value)
                    SDL_EnableScreenSaver();
                else
                    SDL_DisableScreenSaver();
            }
        }

        public static Window CreateWindow(string title, int x, int y, int w, int h, WindowFlags flags)
          => Window.Create(title, x, y, w, h, flags);

        public static (Window window, Renderer renderer) CreateWindowAndRenderer(int width, int height, WindowFlags flags)
        {
            Window window;
            Renderer renderer;
            ErrorIfNegative(SDL_CreateWindowAndRenderer(width, height, flags, out window, out renderer));
            return (window, renderer);
        }
    }
}
