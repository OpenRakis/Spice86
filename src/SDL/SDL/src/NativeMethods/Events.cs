using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport("SDL2")]
        public static extern void SDL_AddEventWatch(/*SDL_EventFilter*/ IntPtr filter, IntPtr userdata);

        [DllImport("SDL2")]
        public static extern void SDL_DelEventWatch(/*SDL_EventFilter*/ IntPtr filter, IntPtr userdata);

        [DllImport("SDL2")]
        public static extern byte SDL_EventState(uint type, int state);

        [DllImport("SDL2")]
        public static extern void SDL_FilterEvents(/*SDL_EventFilter*/ IntPtr filter, IntPtr userdata);

        [DllImport("SDL2")]
        public static extern void SDL_FlushEvent(uint type);

        [DllImport("SDL2")]
        public static extern void SDL_FlushEvents(uint minType, uint maxType);

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_GetEventFilter(out /*SDL_EventFilter*/ IntPtr filter, out IntPtr userdata);

        [DllImport("SDL2")]
        public static extern int SDL_GetNumTouchDevices();

        [DllImport("SDL2")]
        public static extern int SDL_GetNumTouchFingers(long touchID);

        [DllImport("SDL2")]
        public static extern long SDL_GetTouchDevice(int index);

        [DllImport("SDL2")]
        public static extern SDL_Finger* SDL_GetTouchFinger(long touchID, int index);

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_HasEvent(uint type);

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_HasEvents(uint minType, uint maxType);

        [DllImport("SDL2")]
        public static extern int SDL_LoadDollarTemplates(long touchId, RWOps src);

        [DllImport("SDL2")]
        public static extern int SDL_PeepEvents(
            Event* events,
            int numevents,
            SDL_eventaction action,
            uint minType,
            uint maxType
        );

        [DllImport("SDL2")]
        public static extern int SDL_PollEvent(out Event @event);

        [DllImport("SDL2")]
        public static extern void SDL_PumpEvents();

        [DllImport("SDL2")]
        public static extern int SDL_PushEvent(in Event @event);

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_QuitRequested();

        [DllImport("SDL2")]
        public static extern int SDL_RecordGesture(long touchId);

        [DllImport("SDL2")]
        public static extern uint SDL_RegisterEvents(int numevents);

        [DllImport("SDL2")]
        public static extern int SDL_SaveAllDollarTemplates(RWOps dst);

        [DllImport("SDL2")]
        public static extern int SDL_SaveDollarTemplate(long gestureId, RWOps dst);

        [DllImport("SDL2")]
        public static extern void SDL_SetEventFilter(/*SDL_EventFilter*/ IntPtr filter, IntPtr userdata);

        [DllImport("SDL2")]
        public static extern int SDL_WaitEvent(out Event @event);

        [DllImport("SDL2")]
        public static extern int SDL_WaitEventTimeout(out Event @event, int timeout);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SDL_EventFilter(IntPtr userdata, ref Event @event);

        public enum SDL_eventaction
        {
            Add,
            Peek,
            Get,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_Finger
        {
            public long id;
            public float x, y, pressure;
        }
    }

    public enum EventType : uint
    {
        FirstEvent = 0,

        Quit = 0x100,

        AppTerminating,
        AppLowMemory,
        AppWillEnterBackground,
        AppDidEnterBackground,
        AppWillEnterForeground,
        AppDidEnterForeground,

        WindowEvent = 0x200,
        SysWMEvent,

        KeyDown = 0x300,
        KeyUp,
        TextEditing,
        TextInput,
        KeymapChanged,

        MouseMotion = 0x400,
        MouseButtonDown,
        MouseButtonUp,
        MouseWheel,

        JoyAxisMotion = 0x600,
        JoyBallMotion,
        JoyHatMotion,
        JoyButtonDown,
        JoyButtonUp,
        JoyDeviceAdded,
        JoyDeviceRemoved,

        ControllerAxisMotion = 0x650,
        ControllerButtonDown,
        ControllerButtonUp,
        ControllerDeviceAdded,
        ControllerDeviceRemoved,
        ControllerDeviceRemapped,

        FingerDown = 0x700,
        FingerUp,
        FingerMotion,

        DollarGesture = 0x800,
        DollarRecord,
        MultiGesture,

        ClipboardUpdate = 0x900,

        DropFile = 0x1000,
        DropText,
        DropBegin,
        DropComplete,

        AudioDeviceAdded = 0x1100,
        AudioDeviceRemoved,

        RenderTargetsReset = 0x2000,
        RenderDeviceReset,

        UserEvent = 0x8000,

        LastEvent = 0xFFFF,
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct Event
    {
        [FieldOffset(0)]
        public EventType type;
        [FieldOffset(0)]
        public CommonEvent common;
        [FieldOffset(0)]
        public WindowEvent window;
        [FieldOffset(0)]
        public KeyboardEvent keyboard;
        [FieldOffset(0)]
        public TextEditingEvent edit;
        [FieldOffset(0)]
        public TextInputEvent text;
        [FieldOffset(0)]
        public MouseMotionEvent motion;
        [FieldOffset(0)]
        public MouseButtonEvent button;
        [FieldOffset(0)]
        public MouseWheelEvent wheel;
        [FieldOffset(0)]
        public JoyAxisEvent jaxis;
        [FieldOffset(0)]
        public JoyHatEvent jhat;
        [FieldOffset(0)]
        public JoyBallEvent jball;
        [FieldOffset(0)]
        public JoyDeviceEvent jdevice;
        [FieldOffset(0)]
        public ControllerAxisEvent caxis;
        [FieldOffset(0)]
        public ControllerButtonEvent cbutton;
        [FieldOffset(0)]
        public ControllerDeviceEvent cdevice;
        [FieldOffset(0)]
        public AudioDeviceEvent adevice;
        [FieldOffset(0)]
        public QuitEvent quit;
        [FieldOffset(0)]
        public UserEvent user;
        [FieldOffset(0)]
        public SysWMEvent syswm;
        [FieldOffset(0)]
        public TouchFingerEvent tfinger;
        [FieldOffset(0)]
        public MultiGestureEvent mgesture;
        [FieldOffset(0)]
        public DollarGestureEvent dgesture;
        [FieldOffset(0)]
        public DropEvent drop;
        [FieldOffset(0)]
        fixed byte padding[56];

        public override string ToString()
        {
            switch (type)
            {
                case EventType.Quit:
                    return this.quit.ToString();

                case EventType.AppTerminating:
                case EventType.AppLowMemory:
                case EventType.AppWillEnterBackground:
                case EventType.AppDidEnterBackground:
                case EventType.AppWillEnterForeground:
                case EventType.AppDidEnterForeground:
                    return this.common.ToString();

                case EventType.WindowEvent:
                    return this.window.ToString();
                case EventType.SysWMEvent:
                    return this.syswm.ToString();

                case EventType.KeyDown:
                case EventType.KeyUp:
                    return this.keyboard.ToString();
                case EventType.TextEditing:
                    return this.edit.ToString();
                case EventType.TextInput:
                    return this.text.ToString();
                case EventType.KeymapChanged:
                    return this.common.ToString();

                case EventType.MouseMotion:
                    return this.motion.ToString();
                case EventType.MouseButtonDown:
                case EventType.MouseButtonUp:
                    return this.button.ToString();
                case EventType.MouseWheel:
                    return this.wheel.ToString();

                case EventType.JoyAxisMotion:
                    return this.jaxis.ToString();
                case EventType.JoyBallMotion:
                    return this.jball.ToString();
                case EventType.JoyHatMotion:
                    return this.jhat.ToString();
                case EventType.JoyButtonDown:
                case EventType.JoyButtonUp:
                    return this.button.ToString();
                case EventType.JoyDeviceAdded:
                case EventType.JoyDeviceRemoved:
                    return this.jdevice.ToString();

                case EventType.ControllerAxisMotion:
                    return this.caxis.ToString();
                case EventType.ControllerButtonDown:
                case EventType.ControllerButtonUp:
                    return this.cbutton.ToString();
                case EventType.ControllerDeviceAdded:
                case EventType.ControllerDeviceRemoved:
                case EventType.ControllerDeviceRemapped:
                    return this.cdevice.ToString();

                case EventType.FingerDown:
                case EventType.FingerUp:
                case EventType.FingerMotion:
                    return this.tfinger.ToString();

                case EventType.DollarGesture:
                case EventType.DollarRecord:
                    return this.dgesture.ToString();
                case EventType.MultiGesture:
                    return this.mgesture.ToString();

                case EventType.ClipboardUpdate:
                    return this.common.ToString();

                case EventType.DropFile:
                case EventType.DropText:
                case EventType.DropBegin:
                case EventType.DropComplete:
                    return drop.ToString();

                case EventType.AudioDeviceAdded:
                case EventType.AudioDeviceRemoved:
                    return adevice.ToString();

                case EventType.RenderTargetsReset:
                case EventType.RenderDeviceReset:
                    return this.common.ToString();

                default:
                    if (type >= EventType.UserEvent)
                        return user.ToString();
                    else
                        return $"[{type}ts={common.timestamp})]";
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CommonEvent
    {
        public EventType type;
        public uint timestamp;

        public override string ToString()
        {
            return $"[{type}({timestamp})]";
        }
    }

    public enum WindowEventType : byte
    {
        None,
        Shown,
        Hidden,
        Exposed,
        Moved,
        Resized,
        SizeChanged,
        Minimized,
        Maximized,
        Restored,
        Enter,
        Leave,
        FocusGained,
        FocusLost,
        Close,
        TakeFocus,
        HitTest,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowEvent
    {
        public EventType type;
        public uint timestamp;
        public uint windowID;
        public WindowEventType @event;
        byte padding1, padding2, padding3;
        int data1, data2;

        public override string ToString()
        {
            return $"[{type}/{@event}(ts={timestamp},w={windowID}),data1={data1},data2={data2}]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardEvent
    {
        public EventType type;
        public uint timestamp;
        public uint windowID;
        public byte state;
        public byte repeat;
        byte padding2, padding3;
        public Keysym keysym;

        public override string ToString()
        {
            return $"[{type}={keysym}(ts={timestamp},w={windowID}),state={state}{(repeat != 0 ? ",repeat" : "")}]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TextEditingEvent
    {
        public EventType type;
        public uint timestamp;
        public uint windowID;
        fixed byte _text[32];
        public int start, length;

        public string text
        {
            get
            {
                fixed (byte* b = _text)
                    return NativeMethods.UTF8ToString(b) ?? "";
            }
        }

        public override string ToString()
        {
            return $"[{type}(ts={timestamp},w={windowID}),text={text},start={start},length={length}]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TextInputEvent
    {
        public EventType type;
        public uint timestamp;
        public uint windowID;
        fixed byte _text[32];

        public string text
        {
            get
            {
                fixed (byte* b = _text)
                    return NativeMethods.UTF8ToString(b) ?? "";
            }
        }

        public override string ToString()
        {
            return $"[{type}(ts={timestamp},w={windowID}),text={text}]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseMotionEvent
    {
        public EventType type;
        public uint timestamp;
        public uint windowID;
        public uint which;
        public uint state;
        public int x, y;
        public int xrel, yrel;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp},w={windowID}),which={which},state={state},xy=({x},{y}),rel=({xrel},{yrel})]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseButtonEvent
    {
        public EventType type;
        public uint timestamp;
        public uint windowID;
        public uint which;
        public byte button, state, clicks;
        byte padding1;
        public int x, y;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp},w={windowID}),which={which},button={button},state={state},clicks={clicks}";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseWheelEvent
    {
        public EventType type;
        public uint timestamp;
        public uint windowID;
        public uint which;
        public int x, y;
        public MouseWheelDirection direction;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp},w={windowID}),which={which},direction={direction},xy=({x},{y})]";
        }
    }

    public enum MouseWheelDirection : uint
    {
        Normal,
        Flipped,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JoyAxisEvent
    {
        public EventType type;
        public uint timestamp;
        public int which;
        public byte axis;
        byte padding1, padding2, padding3;
        public short value;
        ushort padding4;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp}),which={which},axis={axis},value={value}]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JoyBallEvent
    {
        public EventType type;
        public uint timestamp;
        public int which;
        public byte ball;
        byte padding1, padding2, padding3;
        public short xrel, yrel;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp}),which={which},ball={ball},rel=({xrel},{yrel})]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JoyHatEvent
    {
        public EventType type;
        public uint timestamp;
        public int which;
        public byte hat, value;
        byte padding1, padding2;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp}),which={which},hat={hat},value={value}]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JoyButtonEvent
    {
        public EventType type;
        public uint timestamp;
        public int which;
        public byte button, state;
        byte padding1, padding2;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp}),which={which},button={button},state={state}]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JoyDeviceEvent
    {
        public EventType type;
        public uint timestamp;
        public int which;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp}),which={which}]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ControllerAxisEvent
    {
        public EventType type;
        public uint timestamp;
        public int which;
        public byte axis;
        byte padding1, padding2, padding3;
        public short value;
        ushort padding4;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp}),which={which},axis={axis},value={value}]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ControllerButtonEvent
    {
        public EventType type;
        public uint timestamp;
        public int which;
        public byte button, state;
        byte padding1, padding2;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp}),which={which},button={button},state={state}]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ControllerDeviceEvent
    {
        public EventType type;
        public uint timestamp;
        public int which;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp}),which={which}]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioDeviceEvent
    {
        public EventType type;
        public uint timestamp;
        public uint which;
        public byte iscapture;
        byte padding1, padding2, padding3;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp}),which={which}{(iscapture != 0 ? ",capture" : "")}]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TouchFingerEvent
    {
        public EventType type;
        public uint timestamp;
        public long touchId;
        public long fingerId;
        public float x, y, dx, dy, pressure;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp}),touchId={touchId},fingerId={fingerId},xy=({x},{y}),delta=({dx},{dy}),pressure={pressure}]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MultiGestureEvent
    {
        public EventType type;
        public uint timestamp;
        public long touchId;
        public float dTheta, dDist, x, y;
        public ushort numFingers;
        ushort padding;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp}),touchId={touchId},dTheta={dTheta},dDist={dDist},xy=({x},{y})]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DollarGestureEvent
    {
        public EventType type;
        public uint timestamp;
        public long touchId;
        public long gestureId;
        public uint numFingers;
        public float error, x, y;

        public override string ToString()
        {
            return $"[{type}(ts={timestamp}),touchId={touchId},gestureId={gestureId},numFingers={numFingers},error={error},xy=({x},{y})]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DropEvent : IDisposable
    {
        public EventType type;
        public uint timestamp;
        IntPtr _file;
        public string? file => NativeMethods.UTF8ToString((byte*)this._file);
        public uint windowId;

        public void Dispose()
        {
            IntPtr f = System.Threading.Interlocked.Exchange(ref _file, IntPtr.Zero);
            if (f != IntPtr.Zero)
                NativeMethods.SDL_free((void*)f);
        }

        public override string ToString()
        {
            return $"[{type}({timestamp},w={windowId}),file={file}]";
        }
    }



    [StructLayout(LayoutKind.Sequential)]
    public struct QuitEvent
    {
        public EventType type;
        public uint timestamp;

        public override string ToString()
        {
            return $"[{type}({timestamp})]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OSEvent
    {
        public EventType type;
        public uint timestamp;

        public override string ToString()
        {
            return $"[{type}({timestamp})]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UserEvent
    {
        public EventType type;
        public uint timestamp;
        public uint windowID;
        public int code;
        public IntPtr data1, data2;

        public override string ToString()
        {
            return $"[{type}({timestamp},w={windowID}),code={code},data1{data1},data2={data2}]";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SysWMEvent
    {
        public EventType type;
        public uint timestamp;
        public IntPtr msg;

        public override string ToString()
        {
            return $"[{type}({timestamp}),msg={msg}]";
        }
    }
}
