namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Shared.Emulator.Mouse;

/// <summary>
/// Typed helpers for the INT 33h services.
/// </summary>
public sealed class MouseServices : InterruptServiceGroup<MouseInt33Handler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="MouseServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public MouseServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 33h AX=0000h: resets the mouse driver and returns the installed flag and button count in AX and BX.
    /// </summary>
    public (ushort ax, ushort bx) MouseInstalledFlag(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(MouseInstalledFlag), () => Handler.MouseInstalledFlag());
    }

    /// <summary>
    /// INT 33h AX=0001h: shows the mouse cursor.
    /// </summary>
    public void ShowMouseCursor(ushort expectedReturnCs, ushort expectedReturnIp) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(ShowMouseCursor), () => Handler.ShowMouseCursor());
    }

    /// <summary>
    /// INT 33h AX=0002h: hides the mouse cursor.
    /// </summary>
    public void HideMouseCursor(ushort expectedReturnCs, ushort expectedReturnIp) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(HideMouseCursor), () => Handler.HideMouseCursor());
    }

    /// <summary>
    /// INT 33h AX=0003h: returns the button state in BX and the cursor position in CX and DX.
    /// </summary>
    public (ushort cx, ushort dx, ushort bx) GetMousePositionAndStatus(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetMousePositionAndStatus), () => Handler.GetMousePositionAndStatus());
    }

    /// <summary>
    /// INT 33h AX=0004h: sets the cursor position to CX and DX.
    /// </summary>
    public void SetMouseCursorPosition(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SetMouseCursorPosition), () => Handler.SetMouseCursorPosition(cx, dx));
    }

    /// <summary>
    /// INT 33h AX=0005h: returns the press counter of button BX in AX and its last position in CX and DX.
    /// </summary>
    public (ushort ax, ushort cx, ushort dx, ushort bx) QueryButtonPressedCounter(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        (ushort ax, ushort resultBx, ushort cx, ushort dx) = CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(QueryButtonPressedCounter), () => Handler.QueryButtonPressedCounter(bx));
        return (ax, cx, dx, resultBx);
    }

    /// <summary>
    /// INT 33h AX=0006h: returns the release counter of button BX in AX and its last position in CX and DX.
    /// </summary>
    public (ushort ax, ushort cx, ushort dx, ushort bx) QueryButtonReleasedCounter(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        (ushort ax, ushort resultBx, ushort cx, ushort dx) = CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(QueryButtonReleasedCounter), () => Handler.QueryButtonReleasedCounter(bx));
        return (ax, cx, dx, resultBx);
    }

    /// <summary>
    /// INT 33h AX=0007h: sets the horizontal cursor range to CX..DX.
    /// </summary>
    public void SetMouseHorizontalMinMaxPosition(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SetMouseHorizontalMinMaxPosition), () => Handler.SetMouseHorizontalMinMaxPosition(cx, dx));
    }

    /// <summary>
    /// INT 33h AX=0008h: sets the vertical cursor range to CX..DX.
    /// </summary>
    public void SetMouseVerticalMinMaxPosition(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SetMouseVerticalMinMaxPosition), () => Handler.SetMouseVerticalMinMaxPosition(cx, dx));
    }

    /// <summary>
    /// INT 33h AX=000Bh: returns the mickey motion since the last call in CX and DX.
    /// </summary>
    public (ushort cx, ushort dx) GetMotionDistance(ushort expectedReturnCs, ushort expectedReturnIp) {
        (short x, short y) = CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetMotionDistance), () => Handler.GetMotionDistance());
        return ((ushort)x, (ushort)y);
    }

    /// <summary>
    /// INT 33h AX=000Ch: installs the event subroutine at ES:DX for the event mask in CX.
    /// </summary>
    public void SetMouseUserDefinedSubroutine(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort es) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SetMouseUserDefinedSubroutine), () => Handler.SetMouseUserDefinedSubroutine(es, dx, (MouseEventMask)cx));
    }

    /// <summary>
    /// INT 33h AX=000Fh: sets the mickey-to-pixel ratio to CX and DX.
    /// </summary>
    public void SetMouseMickeyPixelRatio(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SetMouseMickeyPixelRatio), () => Handler.SetMouseMickeyPixelRatio(cx, dx));
    }

    /// <summary>
    /// INT 33h AX=0013h: sets the double-speed threshold to DX mickeys per second.
    /// </summary>
    public void SetMouseDoubleSpeedThreshold(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SetMouseDoubleSpeedThreshold), () => Handler.SetMouseDoubleSpeedThreshold(dx));
    }

    /// <summary>
    /// INT 33h AX=0014h: swaps the event subroutine for mask CX with the one at ES:DX, returning the previous one.
    /// </summary>
    public (ushort cx, ushort dx, ushort es) SwapMouseUserDefinedSubroutine(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort es) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SwapMouseUserDefinedSubroutine), () => Handler.SwapMouseUserDefinedSubroutine(cx, es, dx));
    }

    /// <summary>
    /// INT 33h AX=001Ah: sets the mouse sensitivity from BX, CX and DX.
    /// </summary>
    public void SetMouseSensitivity(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort bx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SetMouseSensitivity), () => Handler.SetMouseSensitivity(bx, cx, dx));
    }

    /// <summary>
    /// INT 33h AX=001Bh: returns the mouse sensitivity in BX, CX and DX.
    /// </summary>
    public (ushort cx, ushort dx, ushort bx) GetMouseSensitivity(ushort expectedReturnCs, ushort expectedReturnIp) {
        (ushort bx, ushort cx, ushort dx) = CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetMouseSensitivity), () => Handler.GetMouseSensitivity());
        return (cx, dx, bx);
    }

    /// <summary>
    /// INT 33h AX=001Ch: sets the mouse interrupt rate (BX selects).
    /// </summary>
    public void SetInterruptRate(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SetInterruptRate), () => Handler.SetInterruptRate(bx));
    }

    /// <summary>
    /// INT 33h AX=0021h: software-resets the mouse driver.
    /// </summary>
    public (ushort ax, ushort bx) Reset(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(Reset), () => Handler.Reset());
    }

    /// <summary>
    /// INT 33h AX=0024h: returns the driver version and the mouse type.
    /// </summary>
    public (ushort ax, ushort cx, ushort bx) ReadSoftwareVersionAndMouseType(ushort expectedReturnCs, ushort expectedReturnIp) {
        (ushort? ax, ushort? cx, ushort? bx) = CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(ReadSoftwareVersionAndMouseType), () => Handler.ReadSoftwareVersionAndMouseType());
        return (ax ?? (ushort)EntryOf(nameof(ReadSoftwareVersionAndMouseType)).SelectorValue,
            cx ?? State.CX, bx ?? State.BX);
    }
}
