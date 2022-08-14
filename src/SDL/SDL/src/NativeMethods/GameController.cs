using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport(LibSDL2Name)]
        public static extern int SDL_GameControllerAddMapping(/*const char*/ byte* mappingstring);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GameControllerAddMappingsFromRW(RWOps rw, int freerw);

        [DllImport(LibSDL2Name)]
        public static extern GameControllerMapping SDL_GameControllerMappingForGUID(SDL_JoystickGUID guid);

        [DllImport(LibSDL2Name)]
        public static extern GameControllerMapping SDL_GameControllerMapping(GameController gamecontroller);

        [DllImport(LibSDL2Name)]
        public static extern SDL_Bool SDL_IsGameController(int joystick_index);

        [DllImport(LibSDL2Name)]
        public static extern /*const char*/ byte* SDL_GameControllerNameForIndex(int joystick_index);

        [DllImport(LibSDL2Name)]
        public static extern GameController SDL_GameControllerOpen(int joystick_index);

        [DllImport(LibSDL2Name)]
        public static extern GameController SDL_GameControllerFromInstanceId(int joyid);

        [DllImport(LibSDL2Name)]
        public static extern /*const char*/ byte* SDL_GameControllerName(GameController gamecontroller);

        [DllImport(LibSDL2Name)]
        public static extern SDL_Bool SDL_GameControllerGetAttached(GameController gamecontroller);

        [DllImport(LibSDL2Name)]
        public static extern IntPtr SDL_GameControllerGetJoystick(GameController gamecontroller);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GameControllerEventState(int state);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_GameControllerUpdate();

        [DllImport(LibSDL2Name)]
        public static extern GameControllerAxis SDL_GameControllerGetAxisFromString(/*const char*/ byte* pchString);

        [DllImport(LibSDL2Name)]
        public static extern /*const char*/ byte* SDL_GameControllerGetStringForAxis(GameControllerAxis axis);

        [DllImport(LibSDL2Name)]
        public static extern SDL_GameControllerButtonBind SDL_GameControllerGetBindForAxis(GameController gamecontroller, GameControllerAxis axis);

        [DllImport(LibSDL2Name)]
        public static extern short SDL_GameControllerGetAxis(GameController gamecontroller, GameControllerAxis axis);

        [DllImport(LibSDL2Name)]
        public static extern GameControllerButton SDL_GameControllerGetButtonFromString(/*const char*/ byte* pchString);

        [DllImport(LibSDL2Name)]
        public static extern /*const char*/ byte* SDL_GameControllerGetStringForButton(GameControllerButton button);

        [DllImport(LibSDL2Name)]
        public static extern SDL_GameControllerButtonBind SDL_GameControllerGetBindForButton(GameController gamecontroller, GameControllerButton button);

        [DllImport(LibSDL2Name)]
        public static extern byte SDL_GameControllerGetButton(GameController gamecontroller, GameControllerButton button);

        [DllImport(LibSDL2Name)]
        public static extern byte SDL_GameControllerClose(IntPtr gamecontroller);

        public enum SDL_GameControllerBindType
        {
            None,
            Button,
            Axis,
            Hat
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_GameControllerButtonBind
        {
            public SDL_GameControllerBindType bindType;
            public SDL_GameControllerButtonBindUnion value;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct SDL_GameControllerButtonBindUnion
        {
            [FieldOffset(0)]
            public int button;
            [FieldOffset(0)]
            public int axis;
            [FieldOffset(0)]
            public SDL_GameControllerButtonBindUnionHat hat;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_GameControllerButtonBindUnionHat
        {
            public int hat, hat_mask;
        }
    }

    public enum GameControllerAxis
    {
        Invalid = -1,
        LeftX,
        LeftY,
        RightX,
        RightY,
        TriggerLeft,
        TriggerRight,
        Max,
    }

    public enum GameControllerButton
    {
        Invalid = -1,
        A,
        B,
        X,
        Y,
        Back,
        Guide,
        Start,
        LeftStick,
        RightStick,
        LeftShoulder,
        RightShoulder,
        DPadUp,
        DPadDown,
        DPadLeft,
        DPadRight,
        Max,
    }
}
