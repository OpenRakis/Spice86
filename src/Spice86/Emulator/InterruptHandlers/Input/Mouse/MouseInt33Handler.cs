namespace Spice86.Emulator.InterruptHandlers.Input.Mouse;

using Serilog;

using Spice86.Emulator.Callback;
using Spice86.Emulator.Machine;
using Spice86.Gui;
using Spice86.Utils;

/// <summary>
/// Interface between the mouse and the emulator.<br/>
/// Re-implements int33.<br/>
/// </summary>
public class MouseInt33Handler : InterruptHandler
{
    private static readonly ILogger _logger = Log.Logger.ForContext<MouseInt33Handler>();
    private static readonly int MOUSE_RANGE_X = 639;
    private static readonly int MOUSE_RANGE_Y = 199;
    private readonly Gui gui;
    private int mouseMaxX = MOUSE_RANGE_X;
    private int mouseMaxY = MOUSE_RANGE_Y;
    private int mouseMinX;
    private int mouseMinY;
    private int userCallbackMask;
    private int userCallbackOffset;
    private int userCallbackSegment;

    public MouseInt33Handler(Machine machine, Gui gui) : base(machine)
    {
        this.gui = gui;
        _dispatchTable.Add(0x00, new Callback(0x00, () => this.MouseInstalledFlag()));
        _dispatchTable.Add(0x03, new Callback(0x03, () => this.GetMousePositionAndStatus()));
        _dispatchTable.Add(0x04, new Callback(0x04, () => this.SetMouseCursorPosition()));
        _dispatchTable.Add(0x07, new Callback(0x07, () => this.SetMouseHorizontalMinMaxPosition()));
        _dispatchTable.Add(0x08, new Callback(0x08, () => this.SetMouseVerticalMinMaxPosition()));
        _dispatchTable.Add(0x0C, new Callback(0x0C, () => this.SetMouseUserDefinedSubroutine()));
        _dispatchTable.Add(0x0F, new Callback(0x0F, () => this.SetMouseMickeyPixelRatio()));
        _dispatchTable.Add(0x13, new Callback(0x13, () => this.SetMouseDoubleSpeedThreshold()));
        _dispatchTable.Add(0x14, new Callback(0x14, () => this.SwapMouseUserDefinedSubroutine()));
        _dispatchTable.Add(0x1A, new Callback(0x1A, () => this.SetMouseSensivity()));
    }

    public override int GetIndex()
    {
        return 0x33;
    }

    public void GetMousePositionAndStatus()
    {
        int x = RestrictValue(gui.GetMouseX(), gui.GetWidth(), mouseMinX, mouseMaxX);
        int y = RestrictValue(gui.GetMouseY(), gui.GetHeight(), mouseMinY, mouseMaxY);
        bool leftClick = gui.IsLeftButtonClicked();
        bool rightClick = gui.IsRightButtonClicked();
        _logger.Information("GET MOUSE POSITION AND STATUS {@MouseX}, {@MouseY}, {@LeftClick}, {@RightClick}", x, y, leftClick, rightClick);
        _state.SetCX(x);
        _state.SetDX(y);
        _state.SetBX((leftClick ? 1 : 0) | ((rightClick ? 1 : 0) << 1));
    }

    public void MouseInstalledFlag()
    {
        _logger.Information("MOUSE INSTALLED FLAG");
        _state.SetAX(0xFFFF);

        // 3 buttons
        _state.SetBX(3);
    }

    public override void Run()
    {
        int operation = ConvertUtils.Uint8(_state.GetAX());
        this.Run(operation);
    }

    public void SetMouseCursorPosition()
    {
        int x = _state.GetCX();
        int y = _state.GetDX();
        _logger.Information("SET MOUSE CURSOR POSITION {@MouseX}, {@MouseY}", x, y);
        gui.SetMouseX(x);
        gui.SetMouseY(y);
    }

    public void SetMouseDoubleSpeedThreshold()
    {
        int threshold = _state.GetDX();
        _logger.Information("SET MOUSE DOUBLE SPEED THRESHOLD {@Threshold}", threshold);
    }

    public void SetMouseHorizontalMinMaxPosition()
    {
        this.mouseMinX = _state.GetCX();
        this.mouseMaxX = _state.GetDX();
        _logger.Information("SET MOUSE HORIZONTAL MIN MAX POSITION {@MinX}, {@MaxX}", mouseMinX, mouseMaxX);
    }

    public void SetMouseMickeyPixelRatio()
    {
        int rx = _state.GetCX();
        int ry = _state.GetDX();
        _logger.Information("SET MOUSE MICKEY PIXEL RATIO {@Rx}, {@Ry}", rx, ry);
    }

    public void SetMouseSensivity()
    {
        int horizontalSpeed = _state.GetBX();
        int verticalSpeed = _state.GetCX();
        int threshold = _state.GetDX();
        _logger.Information("SET MOUSE SENSIVITY {@HorizontalSpeed}, {@VerticalSpeed}, {@Threshold}", horizontalSpeed, verticalSpeed, threshold);
    }

    public void SetMouseUserDefinedSubroutine()
    {
        userCallbackMask = _state.GetCX();
        userCallbackSegment = _state.GetES();
        userCallbackOffset = _state.GetDX();
        _logger.Information("SET MOUSE USER DEFINED SUBROUTINE (unimplemented!) {@Mask}, {@Segment}, {@Offset}", userCallbackMask, userCallbackSegment, userCallbackOffset);
    }

    public void SetMouseVerticalMinMaxPosition()
    {
        this.mouseMinY = _state.GetCX();
        this.mouseMaxY = _state.GetDX();
        _logger.Information("SET MOUSE VERTICAL MIN MAX POSITION {@MinY}, {@MaxY}", mouseMinY, mouseMaxY);
    }

    public void SwapMouseUserDefinedSubroutine()
    {
        int newUserCallbackMask = _state.GetCX();
        int newUserCallbackSegment = _state.GetES();
        int newUserCallbackOffset = _state.GetDX();
        _logger.Information("SWAP MOUSE USER DEFINED SUBROUTINE (unimplemented!) {@Mask}, {@Segment}, {@Offset}", newUserCallbackMask, newUserCallbackSegment, newUserCallbackOffset);
        _state.SetCX(userCallbackMask);
        _state.SetES(userCallbackSegment);
        _state.SetDX(userCallbackOffset);
        userCallbackMask = newUserCallbackMask;
        userCallbackOffset = newUserCallbackOffset;
        userCallbackSegment = newUserCallbackSegment;
    }

    /// <summary>
    /// </summary>
    /// <param name="value">Raw value from the GUI</param>
    /// <param name="maxValue">Max that value can be</param>
    /// <param name="min">mix expected by program</param>
    /// <param name="max">max expected by program</param>
    /// <returns></returns>
    private int RestrictValue(int value, int maxValue, int min, int max)
    {
        int range = max - min;
        int valueInRange = (value * range / maxValue);
        if (valueInRange > max)
        {
            return max;
        }

        if (valueInRange < min)
        {
            return min;
        }

        return valueInRange;
    }
}