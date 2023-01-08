using Spice86.Core.DI;
using Spice86.Core.Emulator.Gdb;

namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Serilog;

using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Callback;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

/// <summary>
/// Interface between the mouse and the emulator.<br/>
/// Re-implements int33.<br/>
/// </summary>
public class MouseInt33Handler : InterruptHandler {
    private readonly ILogger _logger;
    private const ushort MOUSE_RANGE_X = 639;
    private const ushort MOUSE_RANGE_Y = 199;
    private readonly IGui? _gui;
    private ushort _mouseMaxX = MOUSE_RANGE_X;
    private ushort _mouseMaxY = MOUSE_RANGE_Y;
    private ushort _mouseMinX;
    private ushort _mouseMinY;
    private ushort _userCallbackMask;
    private ushort _userCallbackOffset;
    private ushort _userCallbackSegment;

    public MouseInt33Handler(Machine machine, ILogger logger, IGui? gui) : base(machine) {
        _logger = logger;
        _gui = gui;
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
    }

    public override byte Index => 0x33;

    public void GetMousePositionAndStatus() {
        if (_gui is null) {
            return;
        }
        ushort x = RestrictValue((ushort)_gui.MouseX, (ushort)_gui.Width, _mouseMinX, _mouseMaxX);
        ushort y = RestrictValue((ushort)_gui.MouseY, (ushort)_gui.Height, _mouseMinY, _mouseMaxY);
        bool leftClick = _gui.IsLeftButtonClicked;
        bool rightClick = _gui.IsRightButtonClicked;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("GET MOUSE POSITION AND STATUS {@MouseX}, {@MouseY}, {@LeftClick}, {@RightClick}", x, y, leftClick, rightClick);
        }
        _state.CX = x;
        _state.DX = y;
        _state.BX = (ushort)((leftClick ? 1 : 0) | (rightClick ? 1 : 0) << 1);
    }

    public void MouseInstalledFlag() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("MOUSE INSTALLED FLAG");
        }
        _state.AX = 0xFFFF;

        // 3 buttons
        _state.BX = 3;
    }

    public override void Run() {
        byte operation = _state.AL;
        Run(operation);
    }

    public void SetMouseCursorPosition() {
        if (_gui is null) {
            return;
        }

        ushort x = _state.CX;
        ushort y = _state.DX;

        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET MOUSE CURSOR POSITION {@MouseX}, {@MouseY}", x, y);
        }

        _gui.MouseX = x;
        _gui.MouseY = y;
    }

    public void SetMouseDoubleSpeedThreshold() {
        ushort threshold = _state.DX;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET MOUSE DOUBLE SPEED THRESHOLD {@Threshold}", threshold);
        }
    }

    public void SetMouseHorizontalMinMaxPosition() {
        _mouseMinX = _state.CX;
        _mouseMaxX = _state.DX;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET MOUSE HORIZONTAL MIN MAX POSITION {@MinX}, {@MaxX}", _mouseMinX, _mouseMaxX);
        }
    }

    public void SetMouseMickeyPixelRatio() {
        ushort rx = _state.CX;
        ushort ry = _state.DX;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET MOUSE MICKEY PIXEL RATIO {@Rx}, {@Ry}", rx, ry);
        }
    }

    public void SetMouseSensitivity() {
        ushort horizontalSpeed = _state.BX;
        ushort verticalSpeed = _state.CX;
        ushort threshold = _state.DX;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET MOUSE SENSITIVITY {@HorizontalSpeed}, {@VerticalSpeed}, {@Threshold}", horizontalSpeed, verticalSpeed, threshold);
        }
    }

    public void SetMouseUserDefinedSubroutine() {
        _userCallbackMask = _state.CX;
        _userCallbackSegment = _state.ES;
        _userCallbackOffset = _state.DX;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET MOUSE USER DEFINED SUBROUTINE (unimplemented!) {@Mask}, {@Segment}, {@Offset}", _userCallbackMask, _userCallbackSegment, _userCallbackOffset);
        }
    }

    public void SetMouseVerticalMinMaxPosition() {
        _mouseMinY = _state.CX;
        _mouseMaxY = _state.DX;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET MOUSE VERTICAL MIN MAX POSITION {@MinY}, {@MaxY}", _mouseMinY, _mouseMaxY);
        }
    }

    public void SwapMouseUserDefinedSubroutine() {
        ushort newUserCallbackMask = _state.CX;
        ushort newUserCallbackSegment = _state.ES;
        ushort newUserCallbackOffset = _state.DX;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SWAP MOUSE USER DEFINED SUBROUTINE (unimplemented!) {@Mask}, {@Segment}, {@Offset}", newUserCallbackMask, newUserCallbackSegment, newUserCallbackOffset);
        }
        _state.CX = _userCallbackMask;
        _state.ES = _userCallbackSegment;
        _state.DX = _userCallbackOffset;
        _userCallbackMask = newUserCallbackMask;
        _userCallbackOffset = newUserCallbackOffset;
        _userCallbackSegment = newUserCallbackSegment;
    }

    public void ShowMouseCursor() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SHOW MOUSE CURSOR (unimplemented!)");
        }
        // @TODO: show mouse cursor
    }

    public void HideMouseCursor() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("HIDE MOUSE CURSOR (unimplemented!)");
        }
        // @TODO: hide mouse cursor
    }

    /// <summary>
    /// Translates values from the GUI into values for the emulated display area
    /// </summary>
    /// <param name="value">Raw value from the GUI</param>
    /// <param name="maxValue">Max of what that value can be</param>
    /// <param name="min">min expected by program</param>
    /// <param name="max">max expected by program</param>
    /// <returns>Value within the emulated display surface area</returns>
    private static ushort RestrictValue(ushort value, ushort maxValue, ushort min, ushort max) {
        int range = max - min;
        ushort valueInRange = (ushort)(value * range / maxValue);
        if (valueInRange > max) {
            return Math.Min(max, value);
        }

        if (valueInRange < min) {
            return Math.Max(min, value);
        }

        return valueInRange;
    }
}