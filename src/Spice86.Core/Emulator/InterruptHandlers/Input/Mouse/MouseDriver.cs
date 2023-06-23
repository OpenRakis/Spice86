namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Shared.Interfaces;

/// <summary>
///     Driver for the mouse.
/// </summary>
public class MouseDriver : IMouseDriver {
    private readonly Cpu _cpu;
    private readonly IGui? _gui;
    private readonly ILoggerService _logger;
    private readonly IMouseDevice _mouseDevice;
    private readonly State _state;

    private readonly IVgaFunctionality _vgaFunctions;
    private MouseRegisters? _savedRegisters;
    private MouseUserCallback _userCallback;
    private VgaMode _vgaMode;
    private int _mouseCursorHidden;

    /// <summary>
    ///     Create a new instance of the mouse driver.
    /// </summary>
    /// <param name="cpu">Cpu instance to use for calling functions and saving/restoring registers</param>
    /// <param name="loggerService">The logger</param>
    /// <param name="mouseDevice">The mouse device / hardware</param>
    /// <param name="gui">The gui to show, hide and position mouse cursor</param>
    /// <param name="vgaFunctions">Access to the current resolution</param>
    public MouseDriver(Cpu cpu, ILoggerService loggerService, IMouseDevice mouseDevice, IGui? gui, IVgaFunctionality vgaFunctions) {
        _cpu = cpu;
        _state = cpu.State;
        _logger = loggerService;
        _mouseDevice = mouseDevice;
        _gui = gui;
        _vgaFunctions = vgaFunctions;

        _vgaFunctions.VideoModeChanged += OnVideoModeChanged;

        Reset();
    }

    /// <inheritdoc />
    public int CurrentMinX { get; set; }

    /// <inheritdoc />
    public int CurrentMaxX { get; set; }

    /// <inheritdoc />
    public int CurrentMaxY { get; set; }

    /// <inheritdoc />
    public int CurrentMinY { get; set; }

    /// <inheritdoc />
    public void Update() {
        if ((_mouseDevice.LastTrigger & _userCallback.TriggerMask) == 0) {
            // Event does not match trigger mask.
            return;
        }
        if (_userCallback.Segment != 0 && _userCallback.Offset != 0) {
            CallUserSubRoutine();
        }
    }

    /// <inheritdoc />
    public MouseUserCallback GetRegisteredCallback() {
        return _userCallback;
    }

    /// <inheritdoc />
    public void RegisterCallback(MouseUserCallback callbackInfo) {
        _userCallback = callbackInfo;
    }

    /// <inheritdoc />
    public void ShowMouseCursor() {
        if (_mouseCursorHidden != 0) {
            _mouseCursorHidden++;
        }
        if (_mouseCursorHidden == 0) {
            _gui?.ShowMouseCursor();
        }
    }

    /// <inheritdoc />
    public void HideMouseCursor() {
        if (_mouseCursorHidden == 0) {
            _gui?.HideMouseCursor();
        }
        _mouseCursorHidden--;
    }

    /// <inheritdoc />
    public void SetCursorPosition(int x, int y) {
        int mouseAreaWidth = CurrentMaxX - CurrentMinX;
        int mouseAreaHeight = CurrentMaxY - CurrentMinY;
        if (_gui != null) {
            _gui.MouseX = x;
            _gui.MouseY = y;
        }
        _mouseDevice.MouseXRelative = (double)x / mouseAreaWidth;
        _mouseDevice.MouseYRelative = (double)y / mouseAreaHeight;
    }

    /// <inheritdoc />
    public int ButtonCount => _mouseDevice.ButtonCount;

    /// <inheritdoc />
    public MouseType MouseType => _mouseDevice.MouseType;

    /// <inheritdoc />
    public MouseStatus GetCurrentMouseStatus() {
        int x = LinearInterpolate(_mouseDevice.MouseXRelative, CurrentMinX, CurrentMaxX);
        int y = LinearInterpolate(_mouseDevice.MouseYRelative, CurrentMinY, CurrentMaxY);
        ushort buttonFlags = (ushort)((_mouseDevice.IsLeftButtonDown ? 1 : 0) | (_mouseDevice.IsRightButtonDown ? 2 : 0) | (_mouseDevice.IsMiddleButtonDown ? 4 : 0));
        return new MouseStatus(x, y, buttonFlags);
    }

    /// <inheritdoc />
    public int HorizontalMickeysPerPixel {
        get => _mouseDevice.HorizontalMickeysPerPixel;
        set => _mouseDevice.HorizontalMickeysPerPixel = value;
    }

    /// <inheritdoc />
    public int VerticalMickeysPerPixel {
        get => _mouseDevice.VerticalMickeysPerPixel;
        set => _mouseDevice.VerticalMickeysPerPixel = value;
    }

    /// <inheritdoc />
    public int DoubleSpeedThreshold {
        get => _mouseDevice.DoubleSpeedThreshold;
        set => _mouseDevice.DoubleSpeedThreshold = value;
    }

    /// <inheritdoc />
    public void RestoreRegisters() {
        if (_savedRegisters == null) {
            return;
        }
        _state.ES = _savedRegisters.Es;
        _state.DS = _savedRegisters.Ds;
        _state.DI = _savedRegisters.Di;
        _state.SI = _savedRegisters.Si;
        _state.BP = _savedRegisters.Bp;
        _state.SP = _savedRegisters.Sp;
        _state.BX = _savedRegisters.Bx;
        _state.DX = _savedRegisters.Dx;
        _state.CX = _savedRegisters.Cx;
        _state.AX = _savedRegisters.Ax;
    }

    /// <inheritdoc />
    public short GetDeltaXMickeys() {
        double deltaXPixels = _mouseDevice.DeltaX * _vgaMode.Width;
        double deltaXMickeys = _mouseDevice.HorizontalMickeysPerPixel * deltaXPixels;
        return (short)deltaXMickeys;
    }

    /// <inheritdoc />
    public short GetDeltaYMickeys() {
        double deltaYPixels = _mouseDevice.DeltaY * _vgaMode.Height;
        double deltaYMickeys = _mouseDevice.VerticalMickeysPerPixel * deltaYPixels;
        return (short)deltaYMickeys;
    }

    /// <inheritdoc />
    public void Reset() {
        _vgaMode = _vgaFunctions.GetCurrentMode();
        SetCursorPosition(_vgaMode.Width / 2, _vgaMode.Height / 2);
        _mouseCursorHidden = -1;
        _gui?.HideMouseCursor();
        CurrentMinX = 0;
        CurrentMinY = 0;
        CurrentMaxX = _vgaMode.Width - 1;
        CurrentMaxY = _vgaMode.Height - 1;
        HorizontalMickeysPerPixel = 8;
        VerticalMickeysPerPixel = 16;
    }

    private void OnVideoModeChanged(object? sender, VideoModeChangedEventArgs e) {
        _vgaMode = e.NewMode;
        Reset();
    }

    private void CallUserSubRoutine() {
        SaveRegisters();
        // Set mouse info
        MouseStatus status = GetCurrentMouseStatus();
        _state.AX = (ushort)_mouseDevice.LastTrigger;
        _state.BX = (ushort)status.ButtonFlags;
        _state.CX = (ushort)status.X;
        _state.DX = (ushort)status.Y;
        _state.SI = (ushort)GetDeltaXMickeys();
        _state.DI = (ushort)GetDeltaYMickeys();

        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} {MethodName}: calling {Segment:X4}:{Offset:X4} with AX={AX:X4}, BX={BX:X4}, CX={CX:X4}, DX={DX:X4}, SI={SI:X4}, DI={DI:X4}",
                nameof(MouseDriver), nameof(CallUserSubRoutine), _userCallback.Segment, _userCallback.Offset, _state.AX, _state.BX, _state.CX, _state.DX, _state.SI, _state.DI);
        }

        // We're going to call the user specific subroutine, and then return to a special callback that will restore the registers.
        const ushort returnCs = CustomMouseInt90Handler.CallAddressSegment;
        const ushort returnIp = CustomMouseInt90Handler.CallAddressOffset;

        _cpu.FarCall(returnCs, returnIp, _userCallback.Segment, _userCallback.Offset);
    }

    private static int LinearInterpolate(double index, int min, int max) {
        return (int)(min + (max - min) * index);
    }

    private void SaveRegisters() {
        _savedRegisters = new MouseRegisters(_state.ES, _state.DS, _state.DI, _state.SI, _state.BP, _state.SP, _state.BX, _state.DX, _state.CX, _state.AX);
    }

    private record MouseRegisters(ushort Es, ushort Ds, ushort Di, ushort Si, ushort Bp, ushort Sp, ushort Bx, ushort Dx, ushort Cx, ushort Ax);
}