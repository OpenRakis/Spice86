using System;
using System.Text;
using System.Drawing;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public class Window : SafeHandle
    {

        protected Window() : base(IntPtr.Zero, true)
        {
        }

        public Window(IntPtr h, bool owned) : base(IntPtr.Zero, owned)
        {
            SetHandle(h);
        }

        public Window(string title, in Point p, WindowFlags flags = WindowFlags.None)
          : this(title, p.x, p.y, flags) { }
        public Window(string title, in Rect r, WindowFlags flags = WindowFlags.None)
          : this(title, r.x, r.y, r.w, r.h, flags) { }

        public Window(string title, int x, int y, int w, int h, WindowFlags flags = WindowFlags.None) : this()
        {
            Window? wnd = Create(title, x, y, w, h, flags);
            SetHandle(wnd.handle);
            wnd.SetHandle(IntPtr.Zero);
        }

        public Window(string title, int w, int h, WindowFlags flags = WindowFlags.None)
          : this(title, WINDOWPOS_UNDEFINED, WINDOWPOS_UNDEFINED, w, h, flags) { }


        public uint ID
        {
            get { return ErrorIfZero(SDL_GetWindowID(this)); }
        }

        public Display Display
        {
            get { return new Display(ErrorIfNegative(SDL_GetWindowDisplayIndex(this))); }
        }

        public WindowFlags Flags
        {
            get { return SDL_GetWindowFlags(this); }
        }

        public PixelDataFormat PixelFormat
        {
            get { return (PixelDataFormat)SDL_GetWindowPixelFormat(this); }
        }

        public unsafe string Title
        {
            get
            {
                return UTF8ToString(SDL_GetWindowTitle(this)) ?? "";
            }
            set
            {
                Span<byte> utf8 = stackalloc byte[Encoding.UTF8.GetByteCount(value) + 1];
                int written = Encoding.UTF8.GetBytes(value, utf8);
                utf8[written] = 0;
                fixed (byte* utf8title = utf8)
                {
                    SDL_SetWindowTitle(this, utf8title);
                }
            }
        }

        public Point Position
        {
            get
            {
                int x, y;
                SDL_GetWindowPosition(this, out x, out y);
                return new Point(x, y);
            }
            set
            {
                SDL_SetWindowPosition(this, value.x, value.y);
            }
        }

        public Size Size
        {
            get
            {
                int w, h;
                SDL_GetWindowSize(this, out w, out h);
                return new Size(w, h);
            }
            set
            {
                SDL_SetWindowSize(this, value.Width, value.Height);
            }
        }

        public Rect Bounds
        {
            get
            {
                return new Rectangle(Position, Size);
            }
            set
            {
                (int x, int y, int w, int h) = value;
                Position = new Point(x, y);
                Size = new Size(w, h);
            }
        }

        public BorderSize? BorderSize
        {
            get
            {
                int top, left, bottom, right;
                int ret = SDL_GetWindowBordersSize(this, out top, out left, out bottom, out right);
                if (ret < 0)
                {
                    SDLException? err = GetError();
                    if (err != null)
                        throw err;
                    else
                        return null;
                }
                return new BorderSize(top, left, bottom, right);
            }
        }

        public float Brightness
        {
            get { return SDL_GetWindowBrightness(this); }
            set { SDL_SetWindowBrightness(this, value); }
        }

        public float Opacity
        {
            get
            {
                float opacity;
                ErrorIfNegative(SDL_GetWindowOpacity(this, out opacity));
                return opacity;
            }
            set { SDL_SetWindowOpacity(this, value); }
        }

        public unsafe DisplayMode DisplayMode
        {
            get
            {
                SDL_DisplayMode mode;
                if (SDL_GetWindowDisplayMode(this, out mode) == 0)
                {
                    return new DisplayMode(mode);
                }
                else
                {
                    throw GetError2();
                }
            }
            set
            {
                SDL_DisplayMode mode;
                mode.format = (uint)value.Format;
                mode.w = value.Width;
                mode.h = value.Height;
                mode.refresh_rate = value.RefreshRate;
                mode.driverdata = IntPtr.Zero;

                ErrorIfNegative(SDL_SetWindowDisplayMode(this, &mode));
            }
        }

        public bool Resizable
        {
            get
            {
                return this.Flags.HasFlag(WindowFlags.Resizable);
            }
            set
            {
                SDL_SetWindowResizable(this, value ? SDL_Bool.True : SDL_Bool.False);
            }
        }

        public Size MinimumSize
        {
            get
            {
                int w, h;
                SDL_GetWindowMinimumSize(this, out w, out h);
                return new Size(w, h);
            }
            set
            {
                SDL_SetWindowMinimumSize(this, value.Width, value.Height);
            }
        }

        public Size MaximumSize
        {
            get
            {
                int w, h;
                SDL_GetWindowMaximumSize(this, out w, out h);
                return new Size(w, h);
            }
            set
            {
                SDL_SetWindowMaximumSize(this, value.Width, value.Height);
            }
        }

        public bool IsGrabbingInput
        {
            get
            {
                return SDL_GetWindowGrab(this) == SDL_Bool.True;
            }
            set
            {
                SDL_SetWindowGrab(this, value ? SDL_Bool.True : SDL_Bool.False);
            }
        }

        public Surface Surface
        {
            get
            {
                return new Surface(ErrorIfNull(SDL_GetWindowSurface(this)), false);
            }
        }

        public void Hide()
        {
            SDL_HideWindow(this);
        }

        public void Show()
        {
            SDL_ShowWindow(this);
        }

        public void Maximize()
        {
            SDL_MaximizeWindow(this);
        }

        public void Minimize()
        {
            SDL_MinimizeWindow(this);
        }

        public void Raise()
        {
            SDL_RaiseWindow(this);
        }

        public void Restore()
        {
            SDL_RestoreWindow(this);
        }

        public void Focus()
        {
            SDL_SetWindowInputFocus(this);
        }

        public void UpdateSurface()
        {
            ErrorIfNegative(SDL_UpdateWindowSurface(this));
        }

        public unsafe void UpdateSurfaceRects(ReadOnlySpan<Rect> rects)
        {
            fixed (Rect* ptr = &MemoryMarshal.GetReference(rects))
            {
                ErrorIfNegative(SDL_UpdateWindowSurfaceRects(this, ptr, rects.Length));
            }
        }

        public void SetBordered(bool bordered)
        {
            SDL_SetWindowBordered(this, bordered ? SDL_Bool.True : SDL_Bool.False);
        }

        public void SetFullscreen(WindowFlags flags)
        {
            ErrorIfNegative(SDL_SetWindowFullscreen(this, flags));
        }

        public void SetModalFor(Window parent)
        {
            ErrorIfNegative(SDL_SetWindowModalFor(this, parent));
        }

        public void SetIcon(Surface surface)
        {
            SDL_SetWindowIcon(this, surface);
        }

        public unsafe void GetDisplayGammaRamp(GammaRamp ramp)
        {
            fixed (ushort* rp = ramp.R != null ? ramp.R.AsSpan() : null)
            fixed (ushort* gp = ramp.G != null ? ramp.G.AsSpan() : null)
            fixed (ushort* bp = ramp.B != null ? ramp.B.AsSpan() : null)
                SDL_GetWindowGammaRamp(this, rp, gp, bp);
        }

        public GammaRamp GetDisplayGammaRamp()
        {
            var ramp = new GammaRamp()
            {
                R = new GammaRampChannel(),
                G = new GammaRampChannel(),
                B = new GammaRampChannel(),
            };
            GetDisplayGammaRamp(ramp);
            return ramp;
        }

        public unsafe void SetDisplayGammaRamp(GammaRamp ramp)
        {
            fixed (ushort* rp = ramp.R != null ? ramp.R.AsSpan() : null)
            fixed (ushort* gp = ramp.G != null ? ramp.G.AsSpan() : null)
            fixed (ushort* bp = ramp.B != null ? ramp.B.AsSpan() : null)
                SDL_SetWindowGammaRamp(this, rp, gp, bp);
        }

        public delegate HitTestResult HitTest(Window window, in Point area);
        SDL_HitTest? ht;
        public void SetHitTest(HitTest? test)
        {
            if (test == null)
            {
                SDL_SetWindowHitTest(this, IntPtr.Zero, IntPtr.Zero);
                ht = null;
            }
            else
            {
                ht = (IntPtr w, in Point p, IntPtr data) =>
                {
                    try
                    {
                        return test(this, p);
                    }
                    catch (Exception e)
                    {
                        SDL.OnUnhandledException(e, true);
                        return HitTestResult.Normal;
                    }
                };
                IntPtr ptr = Marshal.GetFunctionPointerForDelegate<SDL_HitTest>(ht);
                SDL_SetWindowHitTest(this, ptr, IntPtr.Zero);
            }
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        override protected bool ReleaseHandle()
        {
            NativeMethods.SDL_DestroyWindow(this.handle);
            return true;
        }

        public override string ToString()
        {
            return $"Window{ID} \"{Title}\" {(Rectangle)Bounds}";
        }

        public static Window FromID(uint id)
        {
            IntPtr ptr = ErrorIfNull(SDL_GetWindowFromID(id));
            return new Window(ptr, false);
        }

        public static Window? CurrentlyGrabbingInput()
        {
            IntPtr ptr = SDL_GetGrabbedWindow();
            if (ptr != IntPtr.Zero)
                return new Window(ptr, false);
            else
                return null;
        }

        public static int UndefinedPosition => WINDOWPOS_UNDEFINED;
        public static int CenteredPosition => WINDOWPOS_CENTERED;

        public static Window Create(string title, in Size s, WindowFlags flags = WindowFlags.None)
          => Create(title, s.Width, s.Height, flags);

        public static Window Create(string title, in Rect p, WindowFlags flags = WindowFlags.None)
          => Create(title, p.x, p.y, p.w, p.h, flags);

        public static Window Create(string title, int w, int h, WindowFlags flags = WindowFlags.None)
          => Create(title, WINDOWPOS_UNDEFINED, WINDOWPOS_UNDEFINED, w, h, flags);

        public unsafe static Window Create(string title, int x, int y, int w, int h, WindowFlags flags = WindowFlags.None)
        {
            Span<byte> utf8 = stackalloc byte[SL(title)];
            int written = Encoding.UTF8.GetBytes(title, utf8);
            utf8[written] = 0;
            fixed (byte* utf8title = utf8)
            {
                return ErrorIfNull(SDL_CreateWindow(utf8title, x, y, w, h, flags));
            }
        }

        public static Window FromHandle(IntPtr handle)
        {
            return ErrorIfInvalid(SDL_CreateWindowFrom(handle));
        }
    }
}
