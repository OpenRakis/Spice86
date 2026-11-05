namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 33h services.
/// </summary>
public sealed class MouseServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="MouseServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public MouseServices(CSharpOverrideHelper helper) : base(helper, 0x33, ServiceSelector.Ax) {
    }

    /// <summary>
    /// INT 33h AX=0000h: resets the mouse driver and returns the installed flag and button count in AX and BX.
    /// </summary>
    public void MouseInstalledFlag(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x00);
    }

    /// <summary>
    /// INT 33h AX=0001h: shows the mouse cursor.
    /// </summary>
    public void ShowMouseCursor(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x01);
    }

    /// <summary>
    /// INT 33h AX=0002h: hides the mouse cursor.
    /// </summary>
    public void HideMouseCursor(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x02);
    }

    /// <summary>
    /// INT 33h AX=0003h: returns the button state in BX and the cursor position in CX and DX.
    /// </summary>
    public void GetMousePositionAndStatus(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x03);
    }

    /// <summary>
    /// INT 33h AX=0004h: sets the cursor position to CX and DX.
    /// </summary>
    public void SetMouseCursorPosition(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CheckVector();
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x04);
    }

    /// <summary>
    /// INT 33h AX=0005h: returns the press counter of button BX in AX and its last position in CX and DX.
    /// </summary>
    public void QueryButtonPressedCounter(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x05);
    }

    /// <summary>
    /// INT 33h AX=0006h: returns the release counter of button BX in AX and its last position in CX and DX.
    /// </summary>
    public void QueryButtonReleasedCounter(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x06);
    }

    /// <summary>
    /// INT 33h AX=0007h: sets the horizontal cursor range to CX..DX.
    /// </summary>
    public void SetMouseHorizontalMinMaxPosition(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CheckVector();
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x07);
    }

    /// <summary>
    /// INT 33h AX=0008h: sets the vertical cursor range to CX..DX.
    /// </summary>
    public void SetMouseVerticalMinMaxPosition(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CheckVector();
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x08);
    }

    /// <summary>
    /// INT 33h AX=000Bh: returns the mickey motion since the last call in CX and DX.
    /// </summary>
    public void GetMotionDistance(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x0B);
    }

    /// <summary>
    /// INT 33h AX=000Ch: installs the event subroutine at ES:DX for the event mask in CX.
    /// </summary>
    public void SetMouseUserDefinedSubroutine(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort es) {
        CheckVector();
        State.ES = es;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x0C);
    }

    /// <summary>
    /// INT 33h AX=000Fh: sets the mickey-to-pixel ratio to CX and DX.
    /// </summary>
    public void SetMouseMickeyPixelRatio(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CheckVector();
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x0F);
    }

    /// <summary>
    /// INT 33h AX=0013h: sets the double-speed threshold to DX mickeys per second.
    /// </summary>
    public void SetMouseDoubleSpeedThreshold(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx) {
        CheckVector();
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x13);
    }

    /// <summary>
    /// INT 33h AX=0014h: swaps the event subroutine for mask CX with the one at ES:DX, returning the previous one.
    /// </summary>
    public void SwapMouseUserDefinedSubroutine(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort es) {
        CheckVector();
        State.ES = es;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x14);
    }

    /// <summary>
    /// INT 33h AX=001Ah: sets the mouse sensitivity from BX, CX and DX.
    /// </summary>
    public void SetMouseSensitivity(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort bx) {
        CheckVector();
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x1A);
    }

    /// <summary>
    /// INT 33h AX=001Bh: returns the mouse sensitivity in BX, CX and DX.
    /// </summary>
    public void GetMouseSensitivity(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x1B);
    }

    /// <summary>
    /// INT 33h AX=001Ch: sets the mouse interrupt rate (BX selects).
    /// </summary>
    public void SetInterruptRate(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x1C);
    }

    /// <summary>
    /// INT 33h AX=0021h: software-resets the mouse driver.
    /// </summary>
    public void Reset(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x21);
    }

    /// <summary>
    /// INT 33h AX=0024h: returns the driver version and the mouse type.
    /// </summary>
    public void GetSoftwareVersionAndMouseType(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x24);
    }
}
