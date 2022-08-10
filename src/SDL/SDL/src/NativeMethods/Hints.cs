using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport("SDL2")]
        public static extern void SDL_AddHintCallback(
            /* const char */ byte* name,
            /*HintCallback*/ IntPtr callback,
            IntPtr userdata
        );

        [DllImport("SDL2")]
        public static extern void SDL_DelHintCallback(
            /* const char */ byte* name,
            /*HintCallback*/ IntPtr callback,
            IntPtr userdata
        );

        [DllImport("SDL2")]
        public static extern void SDL_ClearHints();

        [DllImport("SDL2")]
        public static extern /*char*/ byte* SDL_GetHint(/* const char*/byte* name);

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_GetHintBoolean(
            /* const char*/byte* name,
            SDL_Bool default_value
        );

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_SetHint(
            /* const char*/byte* name,
            /* const char*/byte* value
        );

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_SetHintWithPriority(
            /* const char*/byte* name,
            /* const char*/byte* value,
            HintPriority priority
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void SDL_HintCallback(
            IntPtr userdata,
            /* const char*/ byte* name,
            /* const char*/ byte* oldValue,
            /* const char*/ byte* newValue
        );
    }

    public enum HintPriority
    {
        Default = 0,
        Normal = 1,
        Override = 2,
    }

    static class HintNames
    {
        public const string FramebufferAcceleration = "SDL_FRAMEBUFFER_ACCELERATION";
        public const string RenderDriver = "SDL_RENDER_DRIVER";
        public const string RenderOpenGLShaders = "SDL_RENDER_OPENGL_SHADERS";
        public const string RenderDirect3DThreadsafe = "SDL_RENDER_DIRECT3D_THREADSAFE";
        public const string RenderDirect3D11Debug = "SDL_RENDER_DIRECT3D11_DEBUG";
        public const string RenderLogicalSizeMode = "SDL_RENDER_LOGICAL_SIZE_MODE";
        public const string RenderScaleQuality = "SDL_RENDER_SCALE_QUALITY";
        public const string RenderVsync = "SDL_RENDER_VSYNC";
        public const string VideoAllowScreensaver = "SDL_VIDEO_ALLOW_SCREENSAVER";
        public const string VideoExternalContext = "SDL_VIDEO_EXTERNAL_CONTEXT";
        public const string VideoX11XvidMode = "SDL_VIDEO_X11_XVIDMODE";
        public const string VideoX11Xinerama = "SDL_VIDEO_X11_XINERAMA";
        public const string VideoX11XRandR = "SDL_VIDEO_X11_XRANDR";
        public const string VideoX11WindowVisualID = "SDL_VIDEO_X11_WINDOW_VISUALID";
        public const string VideoX11NetWMPing = "SDL_VIDEO_X11_NET_WM_PING";
        public const string VideoX11NetWMBypassCompositor = "SDL_VIDEO_X11_NET_WM_BYPASS_COMPOSITOR";
        public const string VideoX11ForceEGL = "SDL_VIDEO_X11_FORCE_EGL";
        public const string WindowFrameUseableWhileCursorHidden = "SDL_WINDOW_FRAME_USABLE_WHILE_CURSOR_HIDDEN";
        public const string WindowsIntresourceIcon = "SDL_WINDOWS_INTRESOURCE_ICON";
        public const string WindowsIntresourceIconSmall = "SDL_WINDOWS_INTRESOURCE_ICON_SMALL";
        public const string WindowsEnableMessageloop = "SDL_WINDOWS_ENABLE_MESSAGELOOP";
        public const string GrabKeyboard = "SDL_GRAB_KEYBOARD";
        public const string MouseDoubleClickTime = "SDL_MOUSE_DOUBLE_CLICK_TIME";
        public const string MouseDoubleClickRadius = "SDL_MOUSE_DOUBLE_CLICK_RADIUS";
        public const string MouseNormalSpeedScale = "SDL_MOUSE_NORMAL_SPEED_SCALE";
        public const string MouseRelativeSpeedScale = "SDL_MOUSE_RELATIVE_SPEED_SCALE";
        public const string MouseRelativeScaling = "SDL_MOUSE_RELATIVE_SCALING";
        public const string MouseRelativeModeWarp = "SDL_MOUSE_RELATIVE_MODE_WARP";
        public const string MouseFocusClickthrough = "SDL_MOUSE_FOCUS_CLICKTHROUGH";
        public const string TouchMouseEvents = "SDL_TOUCH_MOUSE_EVENTS";
        public const string MouseTouchEvents = "SDL_MOUSE_TOUCH_EVENTS";
        public const string VideoMinimizeOnFocusLoss = "SDL_VIDEO_MINIMIZE_ON_FOCUS_LOSS";
        public const string IOSIdleTimerDisabled = "SDL_IOS_IDLE_TIMER_DISABLED";
        public const string IOSOrientations = "SDL_IOS_ORIENTATIONS";
        public const string AppleTVControllerUIEvents = "SDL_APPLE_TV_CONTROLLER_UI_EVENTS";
        public const string AppleTVRemoteAllowRotation = "SDL_APPLE_TV_REMOTE_ALLOW_ROTATION";
        public const string IOSHideHomeIndicator = "SDL_IOS_HIDE_HOME_INDICATOR";
        public const string AccelerometerAsJoystick = "SDL_ACCELEROMETER_AS_JOYSTICK";
        public const string TVRemoteAsJoystick = "SDL_TV_REMOTE_AS_JOYSTICK";
        public const string XInputEnabled = "SDL_XINPUT_ENABLED";
        public const string XInputUseOldJoystickMapping = "SDL_XINPUT_USE_OLD_JOYSTICK_MAPPING";
        public const string Gamecontrollertype = "SDL_GAMECONTROLLERTYPE";
        public const string Gamecontrollerconfig = "SDL_GAMECONTROLLERCONFIG";
        public const string GamecontrollerconfigFile = "SDL_GAMECONTROLLERCONFIG_FILE";
        public const string GamecontrollerIgnoreDevices = "SDL_GAMECONTROLLER_IGNORE_DEVICES";
        public const string GamecontrollerIgnoreDevicesExcept = "SDL_GAMECONTROLLER_IGNORE_DEVICES_EXCEPT";
        public const string GamecontrollerUseButtonLabels = "SDL_GAMECONTROLLER_USE_BUTTON_LABELS";
        public const string JoystickAllowBackgroundEvents = "SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS";
        public const string JoystickHIDAPI = "SDL_JOYSTICK_HIDAPI";
        public const string JoystickHIDAPIPS4 = "SDL_JOYSTICK_HIDAPI_PS4";
        public const string JoystickHIDAPIPS5 = "SDL_JOYSTICK_HIDAPI_PS5";
        public const string JoystickHIDAPIPS4Rumble = "SDL_JOYSTICK_HIDAPI_PS4_RUMBLE";
        public const string JoystickHIDAPISteam = "SDL_JOYSTICK_HIDAPI_STEAM";
        public const string JoystickHIDAPISwitch = "SDL_JOYSTICK_HIDAPI_SWITCH";
        public const string JoystickHIDAPIXbox = "SDL_JOYSTICK_HIDAPI_XBOX";
        public const string JoystickHIDAPICorrelateXInput = "SDL_JOYSTICK_HIDAPI_CORRELATE_XINPUT";
        public const string JoystickHIDAPIGamecube = "SDL_JOYSTICK_HIDAPI_GAMECUBE";
        public const string EnableSteamControllers = "SDL_ENABLE_STEAM_CONTROLLERS";
        public const string JoystickRawinput = "SDL_JOYSTICK_RAWINPUT";
        public const string LinuxJoystickDeadzones = "SDL_LINUX_JOYSTICK_DEADZONES";
        public const string AllowTopmost = "SDL_ALLOW_TOPMOST";
        public const string TimerResolution = "SDL_TIMER_RESOLUTION";
        public const string QTWaylandContentOrientation = "SDL_QTWAYLAND_CONTENT_ORIENTATION";
        public const string QTWaylandWindowFlags = "SDL_QTWAYLAND_WINDOW_FLAGS";
        public const string ThreadStackSize = "SDL_THREAD_STACK_SIZE";
        public const string ThreadPriorityPolicy = "SDL_THREAD_PRIORITY_POLICY";
        public const string ThreadForceRealtimeTimeControl = "SDL_THREAD_FORCE_REALTIME_TIME_CRITICAL";
        public const string VideoHighDPIDisabled = "SDL_VIDEO_HIGHDPI_DISABLED";
        public const string MacCTRLClickEmulateRightClick = "SDL_MAC_CTRL_CLICK_EMULATE_RIGHT_CLICK";
        public const string VideoWinD3DCompiler = "SDL_VIDEO_WIN_D3DCOMPILER";
        public const string VideoWindowSharePixelFormat = "SDL_VIDEO_WINDOW_SHARE_PIXEL_FORMAT";
        public const string WinRTPrivacyPolicyURL = "SDL_WINRT_PRIVACY_POLICY_URL";
        public const string WinRTPrivacyPolicyLabel = "SDL_WINRT_PRIVACY_POLICY_LABEL";
        public const string WinRTHandleBackButton = "SDL_WINRT_HANDLE_BACK_BUTTON";
        public const string VideoMacFullscreenSpaces = "SDL_VIDEO_MAC_FULLSCREEN_SPACES";
        public const string MacBackgroundApp = "SDL_MAC_BACKGROUND_APP";
        public const string AndroidAPKExpansionMainFileVersion = "SDL_ANDROID_APK_EXPANSION_MAIN_FILE_VERSION";
        public const string AndroidAPKExpansionPatchFileVersion = "SDL_ANDROID_APK_EXPANSION_PATCH_FILE_VERSION";
        public const string IMEInternalEditing = "SDL_IME_INTERNAL_EDITING";
        public const string AndroidTrapBackButton = "SDL_ANDROID_TRAP_BACK_BUTTON";
        public const string AndroidBlockOnPause = "SDL_ANDROID_BLOCK_ON_PAUSE";
        public const string AndroidBlockOnPausePulseaudio = "SDL_ANDROID_BLOCK_ON_PAUSE_PAUSEAUDIO";
        public const string ReturnKeyHidesIME = "SDL_RETURN_KEY_HIDES_IME";
        public const string EmscriptenKeyboardElement = "SDL_EMSCRIPTEN_KEYBOARD_ELEMENT";
        public const string EmscriptenAsyncify = "SDL_EMSCRIPTEN_ASYNCIFY";
        public const string NoSignalHandlers = "SDL_NO_SIGNAL_HANDLERS";
        public const string WindowsNoCloseOnALTF4 = "SDL_WINDOWS_NO_CLOSE_ON_ALT_F4";
        public const string BMPSaveLegacyFormat = "SDL_BMP_SAVE_LEGACY_FORMAT";
        public const string WindowsDisableThreadNaming = "SDL_WINDOWS_DISABLE_THREAD_NAMING";
        public const string RPIVideLayer = "SDL_RPI_VIDEO_LAYER";
        public const string VideoDoubleBuffer = "SDL_VIDEO_DOUBLE_BUFFER";
        public const string OpenGLESDriver = "SDL_OPENGL_ES_DRIVER";
        public const string AudioResamplingMode = "SDL_AUDIO_RESAMPLING_MODE";
        public const string AudioCategory = "SDL_AUDIO_CATEGORY";
        public const string RenderBatching = "SDL_RENDER_BATCHING";
        public const string EventLogging = "SDL_EVENT_LOGGING";
        public const string WaveRiffChunkSize = "SDL_WAVE_RIFF_CHUNK_SIZE";
        public const string WaveTruncation = "SDL_WAVE_TRUNCATION";
        public const string WaveFactChunk = "SDL_WAVE_FACT_CHUNK";
        public const string DisplayUsableBounds = "SDL_DISPLAY_USABLE_BOUNDS";
        public const string AudioDeviceAppName = "SDL_AUDIO_DEVICE_APP_NAME";
        public const string AudioDeviceStreamName = "SDL_AUDIO_DEVICE_STREAM_NAME";
        public const string PreferredLocales = "SDL_PREFERRED_LOCALES";
    }
}
