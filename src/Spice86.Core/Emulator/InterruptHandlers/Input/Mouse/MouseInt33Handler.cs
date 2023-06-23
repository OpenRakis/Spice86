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
public class MouseInt33Handler : InterruptHandler {
    private readonly ILoggerService _logger;
    private readonly IMouseDriver _mouseDriver;

    /// <summary>
    ///     Create a new instance of the mouse interrupt handler.
    /// </summary>
    /// <param name="mouseDriver">The mouse driver to handle the actual functionality.</param>
    public MouseInt33Handler(Machine machine, ILoggerService loggerService, IMouseDriver mouseDriver) : base(machine, loggerService) {
        _logger = loggerService.WithLogLevel(LogEventLevel.Verbose);
        _mouseDriver = mouseDriver;
        FillDispatchTable();
    }

    /// <inheritdoc />
    public override byte Index => 0x33;

    /// <inheritdoc />
    public override void Run() {
        byte operation = _state.AL;
        Run(operation);
    }

    public void MouseInstalledFlag() {
        _state.AX = 0xFFFF; // installed
        _state.BX = (ushort)_mouseDriver.ButtonCount;
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 00 {MethodName}: driver installed, {ButtonCount} buttons",
                nameof(MouseInt33Handler), Index, nameof(MouseInstalledFlag), _state.BX);
        }
        _mouseDriver.Reset();
    }

    public void ShowMouseCursor() {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 01 {MethodName}",
                nameof(MouseInt33Handler), Index, nameof(ShowMouseCursor));
        }
        _mouseDriver.ShowMouseCursor();
    }

    public void HideMouseCursor() {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 02 {MethodName}",
                nameof(MouseInt33Handler), Index, nameof(HideMouseCursor));
        }
        _mouseDriver.HideMouseCursor();
    }

    public void GetMousePositionAndStatus() {
        MouseStatus status = _mouseDriver.GetCurrentMouseStatus();
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 03 {MethodName}: {MouseStatus}",
                nameof(MouseInt33Handler), Index, nameof(GetMousePositionAndStatus), status);
        }
        _state.CX = (ushort)status.X;
        _state.DX = (ushort)status.Y;
        _state.BX = (ushort)status.ButtonFlags;
    }

    public void SetMouseCursorPosition() {
        ushort x = _state.CX;
        ushort y = _state.DX;

        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 04 {MethodName}: x = {MouseX}, y = {MouseY}",
                nameof(MouseInt33Handler), Index, nameof(SetMouseCursorPosition), x, y);
        }
        _mouseDriver.SetCursorPosition(x, y);
    }

    public void SetMouseHorizontalMinMaxPosition() {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 07 {MethodName}: min = {Min}, max = {Max}",
                nameof(MouseInt33Handler), Index, nameof(SetMouseHorizontalMinMaxPosition), _state.CX, _state.DX);
        }
        _mouseDriver.CurrentMaxX = _state.DX;
    }

    public void SetMouseVerticalMinMaxPosition() {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 08 {MethodName}: min Y = {MinY}, max Y = {MaxY}",
                nameof(MouseInt33Handler), Index, nameof(SetMouseVerticalMinMaxPosition), _state.CX, _state.DX);
        }
        _mouseDriver.CurrentMinY = _state.CX;
        _mouseDriver.CurrentMaxY = _state.DX;
    }

    public void SetMouseUserDefinedSubroutine() {
        MouseUserCallback callbackInfo = new((MouseEventMask)_state.CX, _state.ES, _state.DX);
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 0C {MethodName}: {@CallbackInfo}",
                nameof(MouseInt33Handler), Index, nameof(SetMouseUserDefinedSubroutine), callbackInfo);
        }
        _mouseDriver.RegisterCallback(callbackInfo);
    }

    public void SetMouseMickeyPixelRatio() {
        ushort horizontal = _state.CX;
        ushort vertical = _state.DX;
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 0F {MethodName}: horizontal = {XRatio} mickeys per 8 pixels, vertical = {YRatio} mickeys per 8 pixels",
                nameof(MouseInt33Handler), Index, nameof(SetMouseMickeyPixelRatio), horizontal, vertical);
        }
        _mouseDriver.HorizontalMickeysPerPixel = (ushort)(horizontal << 3);
        _mouseDriver.VerticalMickeysPerPixel = (ushort)(vertical << 3);
    }

    public void SetMouseDoubleSpeedThreshold() {
        ushort threshold = _state.DX;
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 13 {MethodName}: doubleSpeedThreshold = {Threshold} mickeys per second",
                nameof(MouseInt33Handler), Index, nameof(SetMouseDoubleSpeedThreshold), threshold);
        }
        _mouseDriver.DoubleSpeedThreshold = threshold;
    }

    private void GetMouseSensitivity() {
        int horizontal = _mouseDriver.HorizontalMickeysPerPixel;
        int vertical = _mouseDriver.VerticalMickeysPerPixel;
        int threshold = _mouseDriver.DoubleSpeedThreshold;
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 1B {MethodName}: horizontal = {XRatio} mickeys per pixel, vertical = {YRatio} mickeys per pixel, doubleSpeedThreshold = {Threshold} mickeys per second",
                nameof(MouseInt33Handler), Index, nameof(GetMouseSensitivity), horizontal, vertical, threshold);
        }
        _state.BX = (ushort)horizontal;
        _state.CX = (ushort)vertical;
        _state.DX = (ushort)threshold;
    }

    private void SetMouseSensitivity() {
        ushort horizontal = _state.BX;
        ushort vertical = _state.CX;
        ushort threshold = _state.DX;
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 1A {MethodName}: horizontal = {XRatio} mickeys per pixel, vertical = {YRatio} mickeys per pixel, doubleSpeedThreshold = {Threshold} mickeys per second",
                nameof(MouseInt33Handler), Index, nameof(SetMouseSensitivity), horizontal, vertical, threshold);
        }
        _mouseDriver.HorizontalMickeysPerPixel = horizontal;
        _mouseDriver.VerticalMickeysPerPixel = vertical;
        _mouseDriver.DoubleSpeedThreshold = threshold;
    }

    public void SwapMouseUserDefinedSubroutine() {
        MouseUserCallback newCallback = new((MouseEventMask)_state.CX, _state.ES, _state.DX);
        MouseUserCallback oldCallback = _mouseDriver.GetRegisteredCallback();
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 14 {MethodName}: old: {@OldCallback}, new: {@NewCallback}",
                nameof(MouseInt33Handler), Index, nameof(SwapMouseUserDefinedSubroutine), oldCallback, newCallback);
        }
        _state.CX = (ushort)oldCallback.TriggerMask;
        _state.ES = oldCallback.Segment;
        _state.DX = oldCallback.Offset;
        _mouseDriver.RegisterCallback(newCallback);
    }

    /// <summary>
    ///     Interrupt rate is set by the host OS. NOP.
    /// </summary>
    public void SetInterruptRate() {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 1C {MethodName}: called with rate {Rate} ",
                nameof(MouseInt33Handler), Index, nameof(SetInterruptRate), _state.BX);
        }
        if (_mouseDriver.MouseType == MouseType.InPort) {
            // Set interrupt rate of InPort mouse.
        }
    }

    public void GetSoftwareVersionAndMouseType() {
        if (_mouseDriver.MouseType == MouseType.None) {
            _state.AX = 0xFFFF;
        } else {
            _state.BX = 0x805; //Version 8.05
            _state.CH = _mouseDriver.MouseType switch {
                MouseType.Bus => 0x01,
                MouseType.Serial => 0x02,
                MouseType.InPort => 0x03,
                MouseType.PS2 or MouseType.PS2Wheel => 0x04,
                _ => 0x00
            };
            _state.CL = 0; /* (0=PS/2, 2=IRQ2, 3=IRQ3,...,7=IRQ7,...,0Fh=IRQ15) */
        }
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 24 {MethodName}: reporting version {Major}.{Minor}, mouse type {MouseType} and irq {IRQ}",
                nameof(MouseInt33Handler), Index, nameof(GetSoftwareVersionAndMouseType), _state.BH, _state.BL, _mouseDriver.MouseType, _state.CL);
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
        _dispatchTable.Add(0x0B, new Callback(0x0B, GetMotionDistance));
        _dispatchTable.Add(0x0C, new Callback(0x0C, SetMouseUserDefinedSubroutine));
        _dispatchTable.Add(0x0F, new Callback(0x0F, SetMouseMickeyPixelRatio));
        _dispatchTable.Add(0x13, new Callback(0x13, SetMouseDoubleSpeedThreshold));
        _dispatchTable.Add(0x14, new Callback(0x14, SwapMouseUserDefinedSubroutine));
        _dispatchTable.Add(0x1A, new Callback(0x1A, SetMouseSensitivity));
        _dispatchTable.Add(0x1B, new Callback(0x1B, GetMouseSensitivity));
        _dispatchTable.Add(0x1C, new Callback(0x1C, SetInterruptRate));
        _dispatchTable.Add(0x24, new Callback(0x24, GetSoftwareVersionAndMouseType));
    }

    private void GetMotionDistance() {
        short x = _mouseDriver.GetDeltaXMickeys();
        short y = _mouseDriver.GetDeltaYMickeys();
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT {Int:X2} 0B {MethodName}: x = {X} mickeys, y = {Y} mickeys",
                nameof(MouseInt33Handler), Index, nameof(GetMotionDistance), x, y);
        }
        _state.CX = (ushort)x;
        _state.DX = (ushort)y;
    }
}