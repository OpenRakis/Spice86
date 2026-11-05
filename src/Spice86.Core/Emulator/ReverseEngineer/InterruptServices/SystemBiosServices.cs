namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 15h services.
/// </summary>
public sealed class SystemBiosServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="SystemBiosServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public SystemBiosServices(CSharpOverrideHelper helper) : base(helper, 0x15, ServiceSelector.Ah) {
    }

    /// <summary>
    /// INT 15h AH=06h: unsupported function; reports the error in AH and CF.
    /// </summary>
    public void Unsupported06(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x06);
    }

    /// <summary>
    /// INT 15h AH=24h: toggles the A20 gate or returns its status (AL selects).
    /// </summary>
    public void ToggleA20GateOrGetStatus(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x24);
    }

    /// <summary>
    /// INT 15h AH=41h: unsupported function; reports the error in AH and CF.
    /// </summary>
    public void Unsupported41(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x41);
    }

    /// <summary>
    /// INT 15h AH=4Fh: keyboard intercept; passes the scan code in AL through unchanged.
    /// </summary>
    public void KeyboardIntercept(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x4F);
    }

    /// <summary>
    /// INT 15h AH=50h: DOS/V font subsystem access; reports the function as unsupported.
    /// </summary>
    public void DosVFontSubsystemAccess(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x50);
    }

    /// <summary>
    /// INT 15h AH=83h: starts or cancels a wait interval (AL selects) using the flag at ES:BX.
    /// </summary>
    public void WaitFunction(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx, ushort es) {
        CheckVector();
        State.ES = es;
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x83);
    }

    /// <summary>
    /// INT 15h AH=86h: waits CX:DX microseconds before returning.
    /// </summary>
    public void BiosWait(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CheckVector();
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x86);
    }

    /// <summary>
    /// INT 15h AH=87h: copies CX words in extended memory using the GDT at ES:SI; status in AH with CF.
    /// </summary>
    public void CopyExtendedMemory(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort si, ushort es) {
        CheckVector();
        State.ES = es;
        State.CX = cx;
        State.SI = si;
        Call(expectedReturnCs, expectedReturnIp, 0x87);
    }

    /// <summary>
    /// INT 15h AH=88h: returns the extended memory size in KB in AX.
    /// </summary>
    public void GetExtendedMemorySize(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x88);
    }

    /// <summary>
    /// INT 15h AH=90h: device busy notification; returns immediately.
    /// </summary>
    public void DeviceBusy(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x90);
    }

    /// <summary>
    /// INT 15h AH=91h: device post-complete notification; returns immediately.
    /// </summary>
    public void DevicePost(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x91);
    }

    /// <summary>
    /// INT 15h AH=C0h: unsupported function; reports the error in AH and CF.
    /// </summary>
    public void UnsupportedC0(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0xC0);
    }

    /// <summary>
    /// INT 15h AH=C2h: unsupported function; reports the error in AH and CF.
    /// </summary>
    public void UnsupportedC2(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0xC2);
    }

    /// <summary>
    /// INT 15h AH=C4h: unsupported function; reports the error in AH and CF.
    /// </summary>
    public void UnsupportedC4(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0xC4);
    }
}
