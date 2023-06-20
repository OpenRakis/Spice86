namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Serilog.Events;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
///     Interface between the mouse and the emulator.<br />
///     Re-implements int33.<br />
/// </summary>
public class MouseInt33Handler : InterruptHandler, IMouseInt33Handler {
    private const ushort MouseMinXAbsolute = 0;
    private const ushort MouseMinYAbsolute = 0;
    private readonly IGui? _gui;
    private new readonly ILoggerService _loggerService;
    private readonly IMouseDevice _mouse;

    private ushort _mouseCurrentMaxX;
    private ushort _mouseCurrentMaxY;
    private ushort _mouseCurrentMinX;
    private ushort _mouseCurrentMinY;
    private ushort _userCallbackMask;
    private ushort _userCallbackOffset;
    private ushort _userCallbackSegment;
    private MouseDriverSavedRegisters _savedState;

    public MouseInt33Handler(Machine machine, ILoggerService loggerService, IMouseDevice mouse, IGui? gui, MouseDriverSavedRegisters savedState) : base(machine, loggerService) {
        _loggerService = loggerService.WithLogLevel(LogEventLevel.Verbose);
        _mouse = mouse;
        _gui = gui;
        _savedState = savedState;
        Initialize();
    }

    private void Initialize() {
        FillDispatchTable();
        _mouseCurrentMinX = MouseMinXAbsolute;
        _mouseCurrentMinY = MouseMinYAbsolute;
        _mouseCurrentMaxX = MouseMaxXAbsolute;
        _mouseCurrentMaxY = MouseMaxYAbsolute;
    }

    private ushort MouseMaxXAbsolute => _machine.VgaFunctions.GetCurrentMode().Width;
    private ushort MouseMaxYAbsolute => _machine.VgaFunctions.GetCurrentMode().Height;
    private ushort ScreenWidth => (ushort)(MouseMaxXAbsolute - MouseMinXAbsolute);
    private ushort ScreenHeight => (ushort)(MouseMaxYAbsolute - MouseMinYAbsolute);

    public override byte Index => 0x33;

    public override void Run() {
        byte operation = _state.AL;
        Run(operation);
    }

    public void MouseInstalledFlag() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 00 {MethodName}: driver installed, 3 buttons",
                nameof(MouseInt33Handler), Index, nameof(MouseInstalledFlag));
        }
        _state.AX = 0xFFFF;

        // 3 buttons
        _state.BX = 3;
    }

    public void ShowMouseCursor() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 01 {MethodName}: {Gui}",
                nameof(MouseInt33Handler), Index, nameof(ShowMouseCursor), _gui is null ? "no gui present" : "telling gui to show cursor");
        }
        _gui?.ShowMouseCursor();
    }

    public void HideMouseCursor() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 02 {MethodName}: {Gui}",
                nameof(MouseInt33Handler), Index, nameof(HideMouseCursor), _gui is null ? "no gui present" : "telling gui to hide cursor");
        }
        _gui?.HideMouseCursor();
    }

    public void Update() {
        if (_savedState.Locked) {
            _loggerService.Debug("Mousehandler is locked, returning");
            return;
        }
        _savedState.Lock();
        double deltaXPixels = _mouse.MouseXRelative * ScreenWidth;
        double deltaYPixels = _mouse.MouseYRelative * ScreenHeight;
        ushort deltaXMickeys = (ushort)(_mouse.HorizontalMickeysPerPixel * deltaXPixels);
        ushort deltaYMickeys = (ushort)(_mouse.VerticalMickeysPerPixel * deltaYPixels);
        if (_userCallbackSegment != 0 && _userCallbackOffset != 0 && (_userCallbackMask & 1) != 0) {
            CallUserSubRoutine(_mouse.LastTrigger, deltaXMickeys, deltaYMickeys);
        }
    }

    public void GetMousePositionAndStatus() {
        MouseStatus status = GetCurrentMouseStatus();
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 03 {MethodName}: {MouseStatus}",
                nameof(MouseInt33Handler), Index, nameof(GetMousePositionAndStatus), status);
        }
        _state.CX = status.X;
        _state.DX = status.Y;
        _state.BX = status.ButtonFlags;
    }

    private MouseStatus GetCurrentMouseStatus() {
        ushort xRaw = (ushort)LinearInterpolate(_mouse.MouseXRelative, MouseMinXAbsolute, MouseMaxXAbsolute);
        ushort yRaw = (ushort)LinearInterpolate(_mouse.MouseYRelative, MouseMinYAbsolute, MouseMaxYAbsolute);
        ushort x = ushort.Clamp(xRaw, _mouseCurrentMinX, _mouseCurrentMaxX);
        ushort y = ushort.Clamp(yRaw, _mouseCurrentMinY, _mouseCurrentMaxY);
        ushort buttonFlags = (ushort)((_mouse.IsLeftButtonDown ? 1 : 0) | (_mouse.IsRightButtonDown ? 2 : 0) | (_mouse.IsMiddleButtonDown ? 4 : 0));
        return new MouseStatus(x, y, buttonFlags);
    }

    public record struct MouseStatus(ushort X, ushort Y, ushort ButtonFlags) {
        /// <inheritdoc />
        public override string ToString() {
            return string.Format("x = {0}, y = {1}, , leftButton = {2}, rightButton = {3}, middleButton = {4}"
                , X, Y, (ButtonFlags & 1) == 1 ? "down" : "up", (ButtonFlags & 2) == 2 ? "down" : "up", (ButtonFlags & 4) == 4 ? "down" : "up");
        }
    };

    public void SetMouseCursorPosition() {
        if (_gui is null) {
            return;
        }

        ushort x = _state.CX;
        ushort y = _state.DX;

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 04 {MethodName}: x = {MouseX}, y = {MouseY}",
                nameof(MouseInt33Handler), Index, nameof(SetMouseCursorPosition), x, y);
        }

        _gui.MouseX = x;
        _gui.MouseY = y;
    }

    public void SetMouseHorizontalMinMaxPosition() {
        _mouseCurrentMinX = _state.CX;
        _mouseCurrentMaxX = _state.DX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 07 {MethodName}: min = {Min}, max = {Max}",
                nameof(MouseInt33Handler), Index, nameof(SetMouseHorizontalMinMaxPosition), _mouseCurrentMinX, _mouseCurrentMaxX);
        }
    }

    public void SetMouseVerticalMinMaxPosition() {
        _mouseCurrentMinY = _state.CX;
        _mouseCurrentMaxY = _state.DX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 08 {MethodName}: min Y = {MinY}, max Y = {MaxY}",
                nameof(MouseInt33Handler), Index, nameof(SetMouseVerticalMinMaxPosition), _mouseCurrentMinY, _mouseCurrentMaxY);
        }
    }

    public void SetMouseUserDefinedSubroutine() {
        _userCallbackMask = _state.CX;
        _userCallbackSegment = _state.ES;
        _userCallbackOffset = _state.DX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 0C {MethodName}: mask = {Mask:X2}, segment = {Segment:X4}, offset = {Offset:X4}",
                nameof(MouseInt33Handler), Index, nameof(SetMouseUserDefinedSubroutine), _userCallbackMask, _userCallbackSegment, _userCallbackOffset);
        }
    }

    public void SetMouseMickeyPixelRatio() {
        ushort horizontal = _state.CX;
        ushort vertical = _state.DX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 0F {MethodName}: horizontal = {XRatio} mickeys per 8 pixels, vertical = {YRatio} mickeys per 8 pixels",
                nameof(MouseInt33Handler), Index, nameof(SetMouseMickeyPixelRatio), horizontal, vertical);
        }
        _mouse.HorizontalMickeysPerPixel = (ushort)(horizontal >> 3);
        _mouse.VerticalMickeysPerPixel = (ushort)(vertical >> 3);
    }

    public void SetMouseDoubleSpeedThreshold() {
        ushort threshold = _state.DX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 13 {MethodName}: doubleSpeedThreshold = {Threshold} mickeys per second",
                nameof(MouseInt33Handler), Index, nameof(SetMouseDoubleSpeedThreshold), threshold);
        }
        _mouse.DoubleSpeedThreshold = threshold;
    }

    public void SetMouseSensitivity() {
        ushort horizontal = _state.BX;
        ushort vertical = _state.CX;
        ushort threshold = _state.DX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 1A {MethodName}: horizontal = {XRatio} mickeys per pixel, vertical = {YRatio} mickeys per pixel, doubleSpeedThreshold = {Threshold} mickeys per second",
                nameof(MouseInt33Handler), Index, nameof(SetMouseSensitivity), horizontal, vertical, threshold);
        }
        _mouse.HorizontalMickeysPerPixel = horizontal;
        _mouse.VerticalMickeysPerPixel = vertical;
        _mouse.DoubleSpeedThreshold = threshold;
    }

    public void SwapMouseUserDefinedSubroutine() {
        ushort newUserCallbackMask = _state.CX;
        ushort newUserCallbackSegment = _state.ES;
        ushort newUserCallbackOffset = _state.DX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 14 {MethodName}: old: mask = {Mask:X2}, segment = {Segment:X4}, offset = {Offset:X4}, new: mask = {NewMask:X2}, segment = {NewSegment:X4}, offset = {NewOffset:X4}",
                nameof(MouseInt33Handler), Index, nameof(SwapMouseUserDefinedSubroutine), _userCallbackMask, _userCallbackSegment, _userCallbackOffset, newUserCallbackMask, newUserCallbackSegment, newUserCallbackOffset);
        }
        _state.CX = _userCallbackMask;
        _state.ES = _userCallbackSegment;
        _state.DX = _userCallbackOffset;
        _userCallbackMask = newUserCallbackMask;
        _userCallbackOffset = newUserCallbackOffset;
        _userCallbackSegment = newUserCallbackSegment;
    }

    /// <summary>
    ///     Interrupt rate is set by the host OS. NOP.
    /// </summary>
    public void SetInterruptRate() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 1C {MethodName}: called with rate {Rate} ",
                nameof(MouseInt33Handler), Index, nameof(SetInterruptRate), _state.BX);
        }
        if (_mouse.MouseType == MouseType.InPort) {
            // Set interrupt rate of InPort mouse.
        }
    }

    public void GetSoftwareVersionAndMouseType() {
        if (_mouse.MouseType == MouseType.None) {
            _state.AX = 0xFFFF;
        } else {
            _state.BX = 0x805; //Version 8.05
            _state.CH = _mouse.MouseType switch {
                MouseType.Bus => 0x01,
                MouseType.Serial => 0x02,
                MouseType.InPort => 0x03,
                MouseType.PS2 or MouseType.PS2Wheel => 0x04,
                _ => 0x00
            };
            _state.CL = 0; /* (0=PS/2, 2=IRQ2, 3=IRQ3,...,7=IRQ7,...,0Fh=IRQ15) */
        }
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 24 {MethodName}: reporting version {Major}.{Minor}, mouse type {MouseType} and irq {IRQ}",
                nameof(MouseInt33Handler), Index, nameof(GetSoftwareVersionAndMouseType), _state.BH, _state.BL, _mouse.MouseType, _state.CL);
        }
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x00, new Callback(0x00, MouseInstalledFlag));
        _dispatchTable.Add(0x01, new Callback(0x01, ShowMouseCursor));
        _dispatchTable.Add(0x02, new Callback(0x02, HideMouseCursor));
        _dispatchTable.Add(0x03, new Callback(0x03, GetMousePositionAndStatus));
        _dispatchTable.Add(0x04, new Callback(0x04, SetMouseCursorPosition));
        _dispatchTable.Add(0x07, new Callback(0x07, SetMouseHorizontalMinMaxPosition));
        _dispatchTable.Add(0x08, new Callback(0x08, SetMouseVerticalMinMaxPosition));
        _dispatchTable.Add(0x0C, new Callback(0x0C, SetMouseUserDefinedSubroutine));
        _dispatchTable.Add(0x0F, new Callback(0x0F, SetMouseMickeyPixelRatio));
        _dispatchTable.Add(0x13, new Callback(0x13, SetMouseDoubleSpeedThreshold));
        _dispatchTable.Add(0x14, new Callback(0x14, SwapMouseUserDefinedSubroutine));
        _dispatchTable.Add(0x1A, new Callback(0x1A, SetMouseSensitivity));
        _dispatchTable.Add(0x1C, new Callback(0x1C, SetInterruptRate));
        _dispatchTable.Add(0x24, new Callback(0x24, GetSoftwareVersionAndMouseType));
    }

    public void CallUserSubRoutine(MouseEventMask trigger, ushort deltaXMickeys, ushort deltaYMickeys) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} {MethodName}: trigger: {Trigger:X2}, delta x: {DeltaX} mickeys, delta y: {DeltaY} mickeys",
                nameof(MouseInt33Handler), nameof(CallUserSubRoutine), (byte)trigger, deltaXMickeys, deltaYMickeys);
        }
        // Save registers
        _loggerService.Debug("Saving state");
        _savedState.Save(_state);
        // Set mouse info
        MouseStatus status = GetCurrentMouseStatus();
        _state.AX = (ushort)trigger;
        _state.BX = status.ButtonFlags;
        _state.CX = status.X;
        _state.DX = status.Y;
        _state.SI = deltaXMickeys;
        _state.DI = deltaYMickeys;
        
        const ushort returnCs = 0xF123;
        const ushort returnIp = 0x0000;

        _memory.SetUint8(0xF1230, 0xFE);
        _memory.SetUint8(0xF1231, 0x38);
        _memory.SetUint8(0xF1232, 0x90);
        _memory.SetUint8(0xF1233, 0xCF);
        
        _machine.Cpu.FarCall(returnCs, returnIp, _userCallbackSegment, _userCallbackOffset);
    }

    private static int LinearInterpolate(double index, int min, int max) {
        return (int)(min + (max - min) * index);
    }
}