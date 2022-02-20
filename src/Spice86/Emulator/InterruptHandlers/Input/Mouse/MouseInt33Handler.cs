namespace Spice86.Emulator.InterruptHandlers.Input.Mouse;

using Serilog;

using Spice86.Emulator.Callback;
using Spice86.Emulator.VM;
using Spice86.UI;

/// <summary>
/// Interface between the mouse and the emulator.<br/>
/// Re-implements int33.<br/>
/// </summary>
public class MouseInt33Handler : InterruptHandler {
    private static readonly ILogger _logger = Log.Logger.ForContext<MouseInt33Handler>();
    private const ushort MOUSE_RANGE_X = 639;
    private const ushort MOUSE_RANGE_Y = 199;
    private readonly IVideoKeyboardMouseIO? _gui;
    private ushort _mouseMaxX = MOUSE_RANGE_X;
    private ushort _mouseMaxY = MOUSE_RANGE_Y;
    private ushort _mouseMinX;
    private ushort _mouseMinY;
    private ushort _userCallbackMask;
    private ushort _userCallbackOffset;
    private ushort _userCallbackSegment;

    public MouseInt33Handler(Machine machine, IVideoKeyboardMouseIO? gui) : base(machine) {
        this._gui = gui;
        _dispatchTable.Add(0x00, new Callback(0x00, this.MouseInstalledFlag));
        _dispatchTable.Add(0x03, new Callback(0x03, this.GetMousePositionAndStatus));
        _dispatchTable.Add(0x04, new Callback(0x04, this.SetMouseCursorPosition));
        _dispatchTable.Add(0x07, new Callback(0x07, this.SetMouseHorizontalMinMaxPosition));
        _dispatchTable.Add(0x08, new Callback(0x08, this.SetMouseVerticalMinMaxPosition));
        _dispatchTable.Add(0x0C, new Callback(0x0C, this.SetMouseUserDefinedSubroutine));
        _dispatchTable.Add(0x0F, new Callback(0x0F, this.SetMouseMickeyPixelRatio));
        _dispatchTable.Add(0x13, new Callback(0x13, this.SetMouseDoubleSpeedThreshold));
        _dispatchTable.Add(0x14, new Callback(0x14, this.SwapMouseUserDefinedSubroutine));
        _dispatchTable.Add(0x1A, new Callback(0x1A, this.SetMouseSensitivity));
    }

    public override byte Index => 0x33;

    public void GetMousePositionAndStatus() {
        if(_gui is null) {
            return;
        }
        ushort x = RestrictValue((ushort)_gui.MouseX, (ushort)_gui.Width, _mouseMinX, _mouseMaxX);
        ushort y = RestrictValue((ushort)_gui.MouseY, (ushort)_gui.Height, _mouseMinY, _mouseMaxY);
        bool leftClick = _gui.IsLeftButtonClicked;
        bool rightClick = _gui.IsRightButtonClicked;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("GET MOUSE POSITION AND STATUS {@MouseX}, {@MouseY}, {@LeftClick}, {@RightClick}", x, y, leftClick, rightClick);
        }
        _state.SetCX(x);
        _state.SetDX(y);
        ushort clickStatus = (ushort)((leftClick ? 1 : 0) | ((rightClick ? 1 : 0) << 1));
        _state.SetBX(clickStatus);
    }

    public void MouseInstalledFlag() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("MOUSE INSTALLED FLAG");
        }
        _state.SetAX(0xFFFF);

        // 3 buttons
        _state.SetBX(3);
    }

    public override void Run() {
        byte operation = _state.GetAL();
        this.Run(operation);
    }

    public void SetMouseCursorPosition() {
        ushort x = _state.GetCX();
        ushort y = _state.GetDX();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET MOUSE CURSOR POSITION {@MouseX}, {@MouseY}", x, y);
        }
        if(_gui is not null) {
            _gui.MouseX = x;
            _gui.MouseY = y;
        }
    }

    public void SetMouseDoubleSpeedThreshold() {
        ushort threshold = _state.GetDX();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET MOUSE DOUBLE SPEED THRESHOLD {@Threshold}", threshold);
        }
    }

    public void SetMouseHorizontalMinMaxPosition() {
        this._mouseMinX = _state.GetCX();
        this._mouseMaxX = _state.GetDX();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET MOUSE HORIZONTAL MIN MAX POSITION {@MinX}, {@MaxX}", _mouseMinX, _mouseMaxX);
        }
    }

    public void SetMouseMickeyPixelRatio() {
        ushort rx = _state.GetCX();
        ushort ry = _state.GetDX();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET MOUSE MICKEY PIXEL RATIO {@Rx}, {@Ry}", rx, ry);
        }
    }

    public void SetMouseSensitivity() {
        ushort horizontalSpeed = _state.GetBX();
        ushort verticalSpeed = _state.GetCX();
        ushort threshold = _state.GetDX();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET MOUSE SENSITIVITY {@HorizontalSpeed}, {@VerticalSpeed}, {@Threshold}", horizontalSpeed, verticalSpeed, threshold);
        }
    }

    public void SetMouseUserDefinedSubroutine() {
        _userCallbackMask = _state.GetCX();
        _userCallbackSegment = _state.GetES();
        _userCallbackOffset = _state.GetDX();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET MOUSE USER DEFINED SUBROUTINE (unimplemented!) {@Mask}, {@Segment}, {@Offset}", _userCallbackMask, _userCallbackSegment, _userCallbackOffset);
        }
    }

    public void SetMouseVerticalMinMaxPosition() {
        this._mouseMinY = _state.GetCX();
        this._mouseMaxY = _state.GetDX();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET MOUSE VERTICAL MIN MAX POSITION {@MinY}, {@MaxY}", _mouseMinY, _mouseMaxY);
        }
    }

    public void SwapMouseUserDefinedSubroutine() {
        ushort newUserCallbackMask = _state.GetCX();
        ushort newUserCallbackSegment = _state.GetES();
        ushort newUserCallbackOffset = _state.GetDX();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SWAP MOUSE USER DEFINED SUBROUTINE (unimplemented!) {@Mask}, {@Segment}, {@Offset}", newUserCallbackMask, newUserCallbackSegment, newUserCallbackOffset);
        }
        _state.SetCX(_userCallbackMask);
        _state.SetES(_userCallbackSegment);
        _state.SetDX(_userCallbackOffset);
        _userCallbackMask = newUserCallbackMask;
        _userCallbackOffset = newUserCallbackOffset;
        _userCallbackSegment = newUserCallbackSegment;
    }

    /// <summary>
    /// </summary>
    /// <param name="value">Raw value from the GUI</param>
    /// <param name="maxValue">Max that value can be</param>
    /// <param name="min">mix expected by program</param>
    /// <param name="max">max expected by program</param>
    /// <returns></returns>
    private static ushort RestrictValue(ushort value, ushort maxValue, ushort min, ushort max) {
        int range = max - min;
        ushort valueInRange = (ushort)(value * range / maxValue);
        if (valueInRange > max) {
            return max;
        }

        if (valueInRange < min) {
            return min;
        }

        return valueInRange;
    }
}