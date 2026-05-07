namespace Spice86.Native;

using Silk.NET.SDL;

using Spice86.Shared.Emulator.Input.Joystick;

using System;
using System.IO;

/// <summary>
/// SDL2-based joystick input adapter. Polls up to two physical joysticks
/// and raises <see cref="JoystickAxisChanged"/> / <see cref="JoystickButtonChanged"/>
/// / <see cref="JoystickHatChanged"/> / <see cref="JoystickConnectionChanged"/>
/// events on transitions, mirroring DOSBox-Staging's SDL2 joystick subsystem
/// (see <c>joystick.cpp</c>). Precompiled SDL2 native libraries are bundled
/// via the same <c>Silk.NET.SDL</c> package the rest of the app uses, so no
/// extra native dependency is required on macOS / Linux / Windows.
/// </summary>
internal sealed class SdlJoystickInput : IDisposable {
    private const int MaxSticks = 2;
    private const int MaxAxesPerStick = 4;
    private const int MaxButtonsPerStick = 4;
    private const int MaxHatsPerStick = 1;

    /// <summary>
    /// SDL axis values are 16-bit signed; normalize to <c>[-1, +1]</c>.
    /// </summary>
    private const float SdlAxisScale = 1.0f / 32767.0f;

    private Sdl? _sdl;
    private bool _initialized;
    private bool _disposed;

    private readonly StickState[] _sticks = new StickState[MaxSticks];

    /// <summary>
    /// Raised when an axis position changes after polling.
    /// </summary>
    public event EventHandler<JoystickAxisEventArgs>? JoystickAxisChanged;

    /// <summary>
    /// Raised when a button transitions between pressed and released.
    /// </summary>
    public event EventHandler<JoystickButtonEventArgs>? JoystickButtonChanged;

    /// <summary>
    /// Raised when the hat (POV) direction changes.
    /// </summary>
    public event EventHandler<JoystickHatEventArgs>? JoystickHatChanged;

    /// <summary>
    /// Raised when a stick is plugged in or removed.
    /// </summary>
    public event EventHandler<JoystickConnectionEventArgs>? JoystickConnectionChanged;

    /// <summary>
    /// Gets a value indicating whether the SDL joystick subsystem
    /// was successfully initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Initializes the SDL joystick subsystem and opens up to two attached
    /// devices. Returns <see langword="false"/> if the SDL native library
    /// is unavailable or no joystick subsystem can be created. Safe to call
    /// once; subsequent calls are no-ops.
    /// </summary>
    public bool TryInitialize() {
        if (_initialized) {
            return true;
        }

        try {
            _sdl = Sdl.GetApi();
        } catch (FileNotFoundException) {
            return false;
        } catch (DllNotFoundException) {
            return false;
        }

        _sdl.SetHint("SDL_NO_SIGNAL_HANDLERS", "1");

        int result = _sdl.InitSubSystem(Sdl.InitJoystick);
        if (result != 0) {
            _sdl.Dispose();
            _sdl = null;
            return false;
        }

        _initialized = true;
        RescanDevices();
        return true;
    }

    /// <summary>
    /// Polls SDL for fresh device state and raises change events for any
    /// axis / button / hat that moved since the previous poll. Should be
    /// called from a UI-thread timer (the host loop) at the desired
    /// polling rate. Safe to call when uninitialized (no-op).
    /// </summary>
    public void Poll() {
        if (!_initialized || _sdl is null) {
            return;
        }

        _sdl.JoystickUpdate();

        for (int stickIndex = 0; stickIndex < MaxSticks; stickIndex++) {
            PollStick(stickIndex);
        }
    }

    /// <summary>
    /// Re-enumerates attached joystick devices, opens any newly plugged
    /// stick (up to <see cref="MaxSticks"/>), and closes sticks that are
    /// no longer attached. Raises connection events on transitions.
    /// Should be called periodically alongside <see cref="Poll"/> to
    /// pick up hot-plug events.
    /// </summary>
    public void RescanDevices() {
        if (!_initialized || _sdl is null) {
            return;
        }

        // Close any sticks that are no longer attached.
        for (int stickIndex = 0; stickIndex < MaxSticks; stickIndex++) {
            ref StickState state = ref _sticks[stickIndex];
            if (!state.IsConnected) {
                continue;
            }

            unsafe {
                if (_sdl.JoystickGetAttached(state.Handle) == SdlBool.True) {
                    continue;
                }
            }

            CloseStick(stickIndex);
        }

        // Open any newly attached sticks until the slots are full.
        int deviceCount = _sdl.NumJoysticks();
        for (int deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++) {
            if (TryFindOpenSlot(out int slot)) {
                OpenStick(slot, deviceIndex);
            } else {
                break;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;

        for (int stickIndex = 0; stickIndex < MaxSticks; stickIndex++) {
            CloseStick(stickIndex);
        }

        if (_sdl is null) {
            return;
        }

        if (_initialized) {
            _sdl.QuitSubSystem(Sdl.InitJoystick);
            _initialized = false;
        }

        _sdl.Dispose();
        _sdl = null;
    }

    private bool TryFindOpenSlot(out int slot) {
        for (int i = 0; i < MaxSticks; i++) {
            if (!_sticks[i].IsConnected) {
                slot = i;
                return true;
            }
        }

        slot = -1;
        return false;
    }

    private unsafe void OpenStick(int stickIndex, int deviceIndex) {
        if (_sdl is null) {
            return;
        }

        Joystick* handle = _sdl.JoystickOpen(deviceIndex);
        if (handle is null) {
            return;
        }

        string deviceName = _sdl.JoystickNameS(handle) ?? string.Empty;
        string deviceGuid = ReadGuidString(_sdl.JoystickGetGUID(handle));

        ref StickState state = ref _sticks[stickIndex];
        state.Handle = handle;
        state.IsConnected = true;
        state.DeviceName = deviceName;
        state.DeviceGuid = deviceGuid;
        for (int i = 0; i < MaxAxesPerStick; i++) {
            state.LastAxis[i] = 0f;
        }
        for (int i = 0; i < MaxButtonsPerStick; i++) {
            state.LastButton[i] = false;
        }
        state.LastHat = JoystickHatDirection.Centered;

        JoystickConnectionChanged?.Invoke(this,
            new JoystickConnectionEventArgs(stickIndex, true, deviceName, deviceGuid));
    }

    private unsafe void CloseStick(int stickIndex) {
        ref StickState state = ref _sticks[stickIndex];
        if (!state.IsConnected) {
            return;
        }

        if (_sdl is not null && state.Handle is not null) {
            _sdl.JoystickClose(state.Handle);
        }

        state.Handle = null;
        state.IsConnected = false;
        state.DeviceName = string.Empty;
        state.DeviceGuid = string.Empty;

        JoystickConnectionChanged?.Invoke(this,
            new JoystickConnectionEventArgs(stickIndex, false, string.Empty, string.Empty));
    }

    private unsafe void PollStick(int stickIndex) {
        if (_sdl is null) {
            return;
        }

        ref StickState state = ref _sticks[stickIndex];
        if (!state.IsConnected || state.Handle is null) {
            return;
        }

        int axisCount = Math.Min(_sdl.JoystickNumAxes(state.Handle), MaxAxesPerStick);
        for (int axis = 0; axis < axisCount; axis++) {
            short raw = _sdl.JoystickGetAxis(state.Handle, axis);
            float normalized = Math.Clamp(raw * SdlAxisScale, -1.0f, 1.0f);
            if (normalized != state.LastAxis[axis]) {
                state.LastAxis[axis] = normalized;
                JoystickAxisChanged?.Invoke(this,
                    new JoystickAxisEventArgs(stickIndex, (JoystickAxis)axis, normalized));
            }
        }

        int buttonCount = Math.Min(_sdl.JoystickNumButtons(state.Handle), MaxButtonsPerStick);
        for (int button = 0; button < buttonCount; button++) {
            bool pressed = _sdl.JoystickGetButton(state.Handle, button) != 0;
            if (pressed != state.LastButton[button]) {
                state.LastButton[button] = pressed;
                JoystickButtonChanged?.Invoke(this,
                    new JoystickButtonEventArgs(stickIndex, button, pressed));
            }
        }

        int hatCount = Math.Min(_sdl.JoystickNumHats(state.Handle), MaxHatsPerStick);
        if (hatCount > 0) {
            byte rawHat = _sdl.JoystickGetHat(state.Handle, 0);
            JoystickHatDirection direction = (JoystickHatDirection)rawHat;
            if (direction != state.LastHat) {
                state.LastHat = direction;
                JoystickHatChanged?.Invoke(this,
                    new JoystickHatEventArgs(stickIndex, direction));
            }
        }
    }

    private static unsafe string ReadGuidString(GUID guid) {
        // Match SDL's lowercase 32-char hex encoding (SDL_JoystickGetGUIDString) without
        // calling the unsafe out-buffer overloads.
        const int byteCount = 16;
        Span<char> hex = stackalloc char[byteCount * 2];
        for (int i = 0; i < byteCount; i++) {
            byte b = guid.Data[i];
            hex[i * 2] = ToHex(b >> 4);
            hex[(i * 2) + 1] = ToHex(b & 0x0F);
        }
        return new string(hex);
    }

    private static char ToHex(int nibble) =>
        (char)(nibble < 10 ? '0' + nibble : 'a' + (nibble - 10));

    private unsafe struct StickState {
        public Joystick* Handle;
        public bool IsConnected;
        public string DeviceName;
        public string DeviceGuid;
        public fixed float LastAxis[MaxAxesPerStick];
        public BoolArray4 LastButton;
        public JoystickHatDirection LastHat;
    }

    /// <summary>
    /// Fixed-size flag array, kept inline so <see cref="StickState"/> stays unmanaged-friendly.
    /// </summary>
    private struct BoolArray4 {
        private byte _b0;
        private byte _b1;
        private byte _b2;
        private byte _b3;

        public bool this[int index] {
            get => index switch {
                0 => _b0 != 0,
                1 => _b1 != 0,
                2 => _b2 != 0,
                3 => _b3 != 0,
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
            set {
                byte v = value ? (byte)1 : (byte)0;
                switch (index) {
                    case 0: _b0 = v; break;
                    case 1: _b1 = v; break;
                    case 2: _b2 = v; break;
                    case 3: _b3 = v; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }
    }
}
