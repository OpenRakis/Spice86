namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
///     Interface between the mouse and the emulator.<br />
///     Re-implements int33.<br />
/// </summary>
public class MouseInt33Handler : InterruptHandler {
    private readonly IMouseDriver _mouseDriver;

    /// <summary>
    ///     Create a new instance of the mouse interrupt handler.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger</param>
    /// <param name="mouseDriver">The mouse driver to handle the actual functionality.</param>
    public MouseInt33Handler(IMemory memory, Cpu cpu, State state, ILoggerService loggerService, IMouseDriver mouseDriver) : base(memory, cpu, state, loggerService) {
        _mouseDriver = mouseDriver;
        FillDispatchTable();
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x33;

    /// <inheritdoc />
    public override void Run() {
        byte operation = _state.AL;
        Run(operation);
    }

    /// <summary>
    ///     Returns: AX    Mouse installed status: 0x0000 = not installed 0xFFFF = installed
    ///     BX    number of mouse buttons
    ///     ──────────────────────────────────────────────────────────────────
    ///     Info: Resets the mouse.  Use this function to determine if mouse support is present.  It performs a hardware and
    ///     software reset (see INT 33H 0021H for a way to performs just a software reset).
    ///     For text-mode applications, this function does the following:
    ///     • Moves the mouse pointer to the center of the screen
    ///     • Hides the pointer (use INT 33H 0001H to display it).
    ///     • Clears any "exclusion area" set via INT 33H 0010H. TODO: not implemented
    ///     • Sets the pointer mask to the default: inverse-attribute of character at pointer (use INT 33H 000aH to change the
    ///     appearance of the pointer). TODO: not implemented
    ///     • Sets the range to the height and width of the entire screen
    ///     (use INT 33H 0007H, and INT 33H 0008H or INT 33H 0010H to limit the mouse pointer display area).
    ///     • Sets up for pointer drawing on video pg 0 (see INT 33H 001dH). TODO: not implemented
    ///     • Enables LightPen emulation (see INT 33H 000dH). TODO: not implemented
    ///     • Sets pointer speed ratio to horizontal: 8 to 8; vertical 8 to 16 and sets the maximum doubling threshold to 64
    ///     mickeys (see INT 33H 001aH).
    /// </summary>
    public void MouseInstalledFlag() {
        _state.AX = 0xFFFF; // installed
        _state.BX = (ushort)_mouseDriver.ButtonCount;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 00 {MethodName}: driver installed, {ButtonCount} buttons",
                nameof(MouseInt33Handler), VectorNumber, nameof(MouseInstalledFlag), _state.BX);
        }
        _mouseDriver.Reset();
    }

    /// <summary>
    /// Returns: none
    /// ──────────────────────────────────────────────────────────────────
    /// Info: This unhides the mouse pointer.  It actually increments an  internal counter used by the mouse support to
    /// determine when to show the pointer.
    /// 
    ///         That counter starts as -1 (after an INT 33H 0000H or 0021H reset).  This call increments it to 0.
    /// 
    /// Whenever the counter is 0, the mouse pointer is displayed and tracked on-screen.  When the counter is 0, subsequent
    /// Show Pointer calls are ignored.  Calls to INT 33H 0002H (hide pointer) decrement the counter.
    /// 
    ///         This logic relieves programs of the burden of global tracking of the hidden/displayed state.  A subroutine
    ///         may always use INT 33H 0001H at the beginning and INT 33H 0002H at the end, without affecting the
    ///         shown/hidden state of the calling routine.
    /// 
    ///         This function also resets the "exclusion area" set via INT 33H 0010H. TODO: not implemented
    /// </summary>
    public void ShowMouseCursor() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 01 {MethodName}",
                nameof(MouseInt33Handler), VectorNumber, nameof(ShowMouseCursor));
        }
        _mouseDriver.ShowMouseCursor();
    }

    /// <summary>
    /// Returns: none
    /// ──────────────────────────────────────────────────────────────────
    /// Info: This removes the mouse pointer from the screen (if it is currently visible).  It actually decrements an
    ///     internal pointer- display cursor.  If that counter is 0 before the call, the mouse pointer is removed from the
    ///     screen.
    /// 
    ///     Use this function before performing any direct writes to the video display (if doing so will overwrite the mouse
    ///     pointer) and call INT 33H 0001H (show ptr) after writing to the screen.
    /// </summary>
    public void HideMouseCursor() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 02 {MethodName}",
                nameof(MouseInt33Handler), VectorNumber, nameof(HideMouseCursor));
        }
        _mouseDriver.HideMouseCursor();
    }

    /// <summary>
    /// Returns:
    ///     BX    Button status:
    ///         bit 0 = left button down   (BX & 1) == 1
    ///         bit 1 = right button down  (BX & 2) == 2
    ///         bit 2 = center button down (BX & 4) == 4
    ///     CX    X coordinate (horizontal)    divide by 8 for text column
    ///     DX    Y coordinate (vertical)      divide by 8 for text line
    /// ──────────────────────────────────────────────────────────────────
    /// Info: This returns the current position of the mouse pointer, and the current status of the mouse buttons.
    /// 
    ///     Rather than constantly polling this function, many programmers prefer to install a mouse event handler via
    ///     INT 33H 000cH or INT 33H 0018H and maintain global variables for instant access to mouse information.
    /// 
    /// Notes: All X,Y coordinates are virtual coordinates and when working with text mode, you must divide each
    ///     value by 8 to get a character column,row.
    /// </summary>
    public void GetMousePositionAndStatus() {
        MouseStatus status = _mouseDriver.GetCurrentMouseStatus();
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 03 {MethodName}: {MouseStatus}",
                nameof(MouseInt33Handler), VectorNumber, nameof(GetMousePositionAndStatus), status);
        }
        _state.CX = (ushort)status.X;
        _state.DX = (ushort)status.Y;
        _state.BX = (ushort)status.ButtonFlags;
    }

    /// <summary>
    ///     CX    X coordinate (horizontal)    multiply text column by 8
    ///     DX    Y coordinate (vertical)      multiply text line by 8
    /// ──────────────────────────────────────────────────────────────────
    /// Info: This sets the driver's internal pointer coordinates.
    /// 
    ///     The pointer moves there, even if it is currently hidden (see INT 33H 0002H) or if X,Y is in the exclusion
    ///     area (see INT 33H 0010H).
    /// 
    ///     If X,Y is outside the range set by INT 33H 0007H and INT 33H 0008H, then the pointer is "pinned" within the
    ///     range rectangle (it will be set to the nearest valid limit).
    /// 
    ///     It is rare to need to move the pointer; the best policy is to let the user do the driving.
    /// 
    /// Notes: All X,Y coordinates are virtual coordinates and when working with text mode, you must divide each value by 8
    ///     to get a character column,row.
    /// </summary>
    public void SetMouseCursorPosition() {
        ushort x = _state.CX;
        ushort y = _state.DX;

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 04 {MethodName}: x = {MouseX}, y = {MouseY}",
                nameof(MouseInt33Handler), VectorNumber, nameof(SetMouseCursorPosition), x, y);
        }
        _mouseDriver.SetCursorPosition(x, y);
    }

    /// <summary>
    ///     CX    minimum Y coordinate (vertical pixel position)
    ///     DX    maximum Y coordinate (vertical pixel position)
    /// Returns: none
    /// ──────────────────────────────────────────────────────────────────
    /// Info: This sets a horizontal range out of which the mouse pointer will not be able to move.  Attempts by the user
    ///     (or the program via INT 33H 0004H) to move to the left of CX or the right of DX will cause the pointer to
    ///     remain at the minimum or maximum value in the range.
    /// 
    ///     Use INT 33H 0008H to limit motion on the vertical axis.
    /// 
    /// Notes: All X,Y coordinates are virtual coordinates and when working with text mode, you must divide each value by
    ///     8 to get a character column,row.
    /// </summary>
    public void SetMouseHorizontalMinMaxPosition() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 07 {MethodName}: min = {Min}, max = {Max}",
                nameof(MouseInt33Handler), VectorNumber, nameof(SetMouseHorizontalMinMaxPosition), _state.CX, _state.DX);
        }
        _mouseDriver.CurrentMinX = _state.CX;
        _mouseDriver.CurrentMaxX = _state.DX;
    }

    /// <summary>
    ///     CX    minimum Y coordinate (vertical pixel position)
    ///     DX    maximum Y coordinate (vertical pixel position)
    /// Returns: none
    /// ──────────────────────────────────────────────────────────────────
    /// Info: This sets a vertical range out of which the mouse pointer will  not be able to move.  Attempts by the user
    ///     (or the program via INT 33H 0004H) to move above CX or below DX will cause the pointer to remain at the
    ///     minimum or maximum value in the range.
    /// 
    ///     Use INT 33H 0007H to limit motion on the horizontal axis.
    /// 
    /// Notes: All X,Y coordinates are virtual coordinates and when working with text mode, you must divide each value by
    ///     8 to get a character column,row.
    /// </summary>
    public void SetMouseVerticalMinMaxPosition() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 08 {MethodName}: min Y = {MinY}, max Y = {MaxY}",
                nameof(MouseInt33Handler), VectorNumber, nameof(SetMouseVerticalMinMaxPosition), _state.CX, _state.DX);
        }
        _mouseDriver.CurrentMinY = _state.CX;
        _mouseDriver.CurrentMaxY = _state.DX;
    }

    /// <summary>
    ///     CX    event mask (events which you want sent to your handler)
    ///         bit 0 = mouse movement           (CX | 01H)
    ///         bit 1 = left button pressed      (CX | 02H)
    ///         bit 2 = left button released     (CX | 04H)
    ///         bit 3 = right button pressed     (CX | 08H)
    ///         bit 4 = right button released    (CX | 10H)
    ///         bit 5 = center button pressed    (CX | 20H)
    ///         bit 6 = center button released   (CX | 40H)
    ///         All events:      CX = 007fH
    ///         Disable handler: CX = 0000H
    ///         ES:DX address of your event handler code
    /// ──────────────────────────────────────────────────────────────────
    /// Returns: none
    /// ──────────────────────────────────────────────────────────────────
    /// Info: This installs a custom mouse event handler.  Specify which events you want to monitor via bit-codes in CX,
    ///     and pass the address of your handler in ES:DX.  When any of the specified events occur, the code at ES:DX will
    ///     get control via a far CALL.
    /// 
    ///     On entry to your handler:
    ///         AX contains a bit mask identifying which event has occurred (it is encoded in the same format as described
    ///             for CX, above).
    /// 
    ///         BX contains the mouse button status:
    ///             bit 0 = left button    (BX & 1) == 1
    ///             bit 1 = right button   (BX & 2) == 2
    ///             bit 2 = center button  (BX & 4) == 4
    /// 
    ///         CX horizontal position (in text mode, divide by 8 for character column)
    ///         DX vertical position (in text mode, divide by 8 for character line)
    /// 
    ///         SI distance of last horizontal motion (mickeys: &lt;0=left; >0=right)
    ///         DI distance of last vertical motion (mickeys: &lt;0=upward, >0=down)
    /// 
    ///         DS mouse driver data segment
    ///             You will need to set up DS to access your own variables.  You need not save/restore CPU registers, since
    ///             the driver does this for you.
    /// 
    ///     Exit your custom handler via a FAR RETurn (not an IRET).
    /// 
    ///     To enable or disable selected events for your handler, use this function again, passing a modified mask in CX.
    ///     To disable all events for the handler, call this function again passing a value of 0000H in CX.
    /// 
    /// Warning: You must disable your custom event handler (call with CX=0) before exiting to DOS.  For TSRs, you must
    ///     enable each time you pop up and disable each time you pop down.
    /// 
    /// Notes: You may prefer to use the more-flexible INT 33H 0014H (exchange handlers) or INT 33H 0018H (install
    ///     mouse+key handler).
    /// </summary>
    public void SetMouseUserDefinedSubroutine() {
        MouseUserCallback callbackInfo = new((MouseEventMask)_state.CX, _state.ES, _state.DX);
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 0C {MethodName}: {@CallbackInfo}",
                nameof(MouseInt33Handler), VectorNumber, nameof(SetMouseUserDefinedSubroutine), callbackInfo);
        }
        _mouseDriver.RegisterCallback(callbackInfo);
    }

    /// <summary>
    ///     CX    desired horizontal mickeys per 8 pixels
    ///     DX    desired vertical mickeys per 8 pixels
    /// ──────────────────────────────────────────────────────────────────
    /// Returns: none
    /// ──────────────────────────────────────────────────────────────────
    /// Info: This sets the base speed at which the pointer moves around the screen, compared to the distance actually
    ///     moved by the mouse.
    /// 
    ///     The mickey-to-pixel ratio is used to convert distance values, such as INT 33H 000bH and values obtained in SI
    ///     and DI by custom event handlers (see INT 33H 000cH), to pixels or character cells.
    /// 
    ///     The pointer may actually move much farther/faster than the base speed specified in this call.  When the mouse
    ///     is moved quickly, the mouse support automatically doubles the ratio (moving the pointer exponentially more
    ///     quickly).  See INT 33H 0013H.
    /// 
    ///     1 mickey = 1/200th of an inch = 0.005 inch.
    /// 
    ///     Note that the values for CX and DX are in character cells (8-pixel units).  The default settings are CX=8 and
    ///     DX=16.  This means that the pointer moves twice as fast horizontally as it does vertically.  It also means that
    ///     a slow, steady 1-inch mouse motion moves the pointer by 25 characters horizontally or 12 vertical lines. The
    ///     calculation goes as follows:
    /// 
    ///         desiredCharsPerInch = 200 / CX
    ///         ...so...
    ///         CX = 200 / desiredCharsPerInch  (thus, CX=8 moves 25 chars)
    /// 
    ///         For graphics mode (i.e., pixel measurements) use:
    /// 
    ///         CX = 25 / desiredPixelsPerInch   (thus, CX=8 moves 200 pixels)
    /// </summary>
    public void SetMouseMickeyPixelRatio() {
        ushort horizontal = _state.CX;
        ushort vertical = _state.DX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 0F {MethodName}: horizontal = {XRatio} mickeys per 8 pixels, vertical = {YRatio} mickeys per 8 pixels",
                nameof(MouseInt33Handler), VectorNumber, nameof(SetMouseMickeyPixelRatio), horizontal, vertical);
        }
        _mouseDriver.HorizontalMickeysPerPixel = (ushort)(horizontal << 3);
        _mouseDriver.VerticalMickeysPerPixel = (ushort)(vertical << 3);
    }

    /// <summary>
    ///    CX    desired double-speed threshold (mickeys per second)
    /// </summary>
    public void SetMouseDoubleSpeedThreshold() {
        ushort threshold = _state.DX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 13 {MethodName}: doubleSpeedThreshold = {Threshold} mickeys per second",
                nameof(MouseInt33Handler), VectorNumber, nameof(SetMouseDoubleSpeedThreshold), threshold);
        }
        _mouseDriver.DoubleSpeedThreshold = threshold;
    }

    private void GetMouseSensitivity() {
        int horizontal = _mouseDriver.HorizontalMickeysPerPixel;
        int vertical = _mouseDriver.VerticalMickeysPerPixel;
        int threshold = _mouseDriver.DoubleSpeedThreshold;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 1B {MethodName}: horizontal = {XRatio} mickeys per pixel, vertical = {YRatio} mickeys per pixel, doubleSpeedThreshold = {Threshold} mickeys per second",
                nameof(MouseInt33Handler), VectorNumber, nameof(GetMouseSensitivity), horizontal, vertical, threshold);
        }
        _state.BX = (ushort)horizontal;
        _state.CX = (ushort)vertical;
        _state.DX = (ushort)threshold;
    }

    private void SetMouseSensitivity() {
        ushort horizontal = _state.BX;
        ushort vertical = _state.CX;
        ushort threshold = _state.DX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 1A {MethodName}: horizontal = {XRatio} mickeys per pixel, vertical = {YRatio} mickeys per pixel, doubleSpeedThreshold = {Threshold} mickeys per second",
                nameof(MouseInt33Handler), VectorNumber, nameof(SetMouseSensitivity), horizontal, vertical, threshold);
        }
        _mouseDriver.HorizontalMickeysPerPixel = horizontal;
        _mouseDriver.VerticalMickeysPerPixel = vertical;
        _mouseDriver.DoubleSpeedThreshold = threshold;
    }

    /// <summary>
    ///     CX    event mask (events which you want sent to your handler)
    ///         bit 0 = mouse movement           (CX | 01H)
    ///         bit 1 = left button pressed      (CX | 02H)
    ///         bit 2 = left button released     (CX | 04H)
    ///         bit 3 = right button pressed     (CX | 08H)
    ///         bit 4 = right button released    (CX | 10H)
    ///         bit 5 = center button pressed    (CX | 20H)
    ///         bit 6 = center button released   (CX | 40H)
    ///         All events:      CX = 007fH
    ///     ES:DX address of your event handler code
    /// ──────────────────────────────────────────────────────────────────
    /// Returns:
    ///     CX    event mask of previous event handler
    ///     ES:DX address of the previously-installed event handler code
    /// ──────────────────────────────────────────────────────────────────
    /// Info: This function works like INT 33H 000cH (which see for details). The only difference is that upon return, you
    ///     obtain the address and event mask of the previously-installed event handler.
    /// 
    ///     This provides a way to install an event handler temporarily; that is, you can install one while performing a
    ///     certain subroutine, then restore the previous one when you exit that subroutine.
    /// 
    ///     This also provides a way to chain event handlers.  Install a handler for all events, and if you get an event
    ///     which you don't really care about, pass it on to the previously-installed handler (assuming its event mask
    ///     shows that it expects the event).
    /// 
    /// Notes: INT 33H 0018H provides a flexible means for installing up to three specialized-event handlers.
    /// </summary>
    public void SwapMouseUserDefinedSubroutine() {
        MouseUserCallback newCallback = new((MouseEventMask)_state.CX, _state.ES, _state.DX);
        MouseUserCallback oldCallback = _mouseDriver.GetRegisteredCallback();
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 14 {MethodName}: old: {@OldCallback}, new: {@NewCallback}",
                nameof(MouseInt33Handler), VectorNumber, nameof(SwapMouseUserDefinedSubroutine), oldCallback, newCallback);
        }
        _state.CX = (ushort)oldCallback.TriggerMask;
        _state.ES = oldCallback.Segment;
        _state.DX = oldCallback.Offset;
        _mouseDriver.RegisterCallback(newCallback);
    }

    /// <summary>
    ///     BX    interrupt rate code:
    ///         1=none;
    ///         2=30 per sec,
    ///         4=50 per sec
    ///         8=100 per sec;
    ///         16=200 per second
    /// ──────────────────────────────────────────────────────────────────
    /// Returns: none
    /// ──────────────────────────────────────────────────────────────────
    /// Info: This sets the rate at which the mouse hardware will interrupt with updated mouse positions and status.
    /// 
    ///     This call is only meaningful for the Inport mouse.  Use INT 33H 0024H to see if an Inport mouse is installed.
    /// 
    ///     Increasing the number of interrupts per second provides more accuracy for the mouse, but makes the foreground
    ///     application slow down.
    /// </summary>
    public void SetInterruptRate() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 1C {MethodName}: called with rate {Rate} ",
                nameof(MouseInt33Handler), VectorNumber, nameof(SetInterruptRate), _state.BX);
        }
        if (_mouseDriver.MouseType == MouseType.InPort) {
            // Set interrupt rate of InPort mouse.
        }
    }

    /// <summary>
    /// Returns:
    ///     BH    major version number
    ///     BL    minor version number (i.e., left of decimal)
    ///     CH    mouse type:
    ///         1 = bus mouse
    ///         2 = serial mouse
    ///         3 = Inport mouse
    ///         4 = PS/2 mouse
    ///         5 = HP mouse
    ///     CL    IRQ number:
    ///         0 = PS/2
    ///         2,3,4,5, or 7 = PC IRQ number
    /// ──────────────────────────────────────────────────────────────────
    /// Info: If you need to use new functions of a recent version of the mouse support, use this function and inspect the
    ///     return values in BH and BL.
    /// 
    ///     The value in CH can be used to see if calls to INT 33H 001cH are meaningful.
    /// </summary>
    public void GetSoftwareVersionAndMouseType() {
        if (_mouseDriver.MouseType == MouseType.None) {
            _state.AX = 0xFFFF;
        } else {
            _state.BX = 0x805; //Version 8.05
            _state.CH = _mouseDriver.MouseType switch {
                MouseType.Bus => 0x01,
                MouseType.Serial => 0x02,
                MouseType.InPort => 0x03,
                MouseType.Ps2 or MouseType.Ps2Wheel => 0x04,
                _ => 0x00
            };
            _state.CL = 0; /* (0=PS/2, 2=IRQ2, 3=IRQ3,...,7=IRQ7,...,0Fh=IRQ15) */
        }
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 24 {MethodName}: reporting version {Major}.{Minor}, mouse type {MouseType} and irq {Irq}",
                nameof(MouseInt33Handler), VectorNumber, nameof(GetSoftwareVersionAndMouseType), _state.BH, _state.BL, _mouseDriver.MouseType, _state.CL);
        }
    }

    private void FillDispatchTable() {
        AddAction(0x00, MouseInstalledFlag);
        AddAction(0x01, ShowMouseCursor);
        AddAction(0x02, HideMouseCursor);
        AddAction(0x03, GetMousePositionAndStatus);
        AddAction(0x04, SetMouseCursorPosition);
        AddAction(0x07, SetMouseHorizontalMinMaxPosition);
        AddAction(0x08, SetMouseVerticalMinMaxPosition);
        AddAction(0x0B, GetMotionDistance);
        AddAction(0x0C, SetMouseUserDefinedSubroutine);
        AddAction(0x0F, SetMouseMickeyPixelRatio);
        AddAction(0x13, SetMouseDoubleSpeedThreshold);
        AddAction(0x14, SwapMouseUserDefinedSubroutine);
        AddAction(0x1A, SetMouseSensitivity);
        AddAction(0x1B, GetMouseSensitivity);
        AddAction(0x1C, SetInterruptRate);
        AddAction(0x24, GetSoftwareVersionAndMouseType);
    }

    private void GetMotionDistance() {
        short x = _mouseDriver.GetDeltaXMickeys();
        short y = _mouseDriver.GetDeltaYMickeys();
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("{ClassName} INT {Int:X2} 0B {MethodName}: x = {X} mickeys, y = {Y} mickeys",
                nameof(MouseInt33Handler), VectorNumber, nameof(GetMotionDistance), x, y);
        }
        _state.CX = (ushort)x;
        _state.DX = (ushort)y;
    }
}