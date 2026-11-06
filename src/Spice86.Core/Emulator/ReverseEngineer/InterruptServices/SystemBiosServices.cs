namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed helpers for the INT 15h services.
/// </summary>
public sealed class SystemBiosServices : InterruptServiceGroup<SystemBiosInt15Handler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="SystemBiosServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public SystemBiosServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 15h AH=06h: unsupported function; reports the error in AH and CF.
    /// </summary>
    public (byte ah, bool carryFlag) Unsupported06(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(Unsupported06), () => Handler.Unsupported(), result => result.carryFlag);
    }

    /// <summary>
    /// INT 15h AH=24h: toggles the A20 gate or returns its status (AL selects).
    /// </summary>
    public (ushort ax, ushort bx, bool carryFlag) ToggleA20GateOrGetStatus(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        (byte? resultAl, byte? ah, ushort? bx, bool? carryFlag) =
            CallDirectWithCarry(expectedReturnCs, expectedReturnIp, nameof(ToggleA20GateOrGetStatus),
                () => Handler.ToggleA20GateOrGetStatus(al),
                result => result.carryFlag ?? State.CarryFlag);
        return ((ushort)(((ah ?? (byte)EntryOf(nameof(ToggleA20GateOrGetStatus)).SelectorValue) << 8)
                | (resultAl ?? al)),
            bx ?? State.BX, carryFlag ?? State.CarryFlag);
    }

    /// <summary>
    /// INT 15h AH=41h: unsupported function; reports the error in AH and CF.
    /// </summary>
    public (byte ah, bool carryFlag) Unsupported41(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(Unsupported41), () => Handler.Unsupported(), result => result.carryFlag);
    }

    /// <summary>
    /// INT 15h AH=4Fh: keyboard intercept; passes the scan code in AL through unchanged.
    /// </summary>
    public bool KeyboardIntercept(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(KeyboardIntercept), () => Handler.KeyboardIntercept(al), carryFlag => carryFlag);
    }

    /// <summary>
    /// INT 15h AH=50h: DOS/V font subsystem access; reports the function as unsupported.
    /// </summary>
    public (byte ah, bool carryFlag) DosVFontSubsystemAccess(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(DosVFontSubsystemAccess), () => Handler.DosVFontSubsystemAccess(), result => result.carryFlag);
    }

    /// <summary>
    /// INT 15h AH=83h: starts or cancels a wait interval (AL selects) using the flag at ES:BX.
    /// </summary>
    public (byte ah, bool carryFlag) WaitFunction(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx, ushort es) {
        (byte? ah, bool carryFlag) = CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(WaitFunction),
            () => Handler.WaitFunction(al, ((uint)cx << 16) | dx,
                new SegmentedAddress(es, bx)),
            result => result.carryFlag);
        return (ah ?? (byte)EntryOf(nameof(WaitFunction)).SelectorValue, carryFlag);
    }

    /// <summary>
    /// INT 15h AH=86h: waits CX:DX microseconds before returning.
    /// </summary>
    public (byte ah, bool carryFlag) BiosWait(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        (byte? ah, bool carryFlag) = CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(BiosWait),
            () => Handler.BiosWait(((uint)cx << 16) | dx),
            result => result.carryFlag);
        return (ah ?? (byte)EntryOf(nameof(BiosWait)).SelectorValue, carryFlag);
    }

    /// <summary>
    /// INT 15h AH=87h: copies CX words in extended memory using the GDT at ES:SI; status in AH with CF.
    /// </summary>
    public (byte ah, bool carryFlag) CopyExtendedMemory(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort si, ushort es) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(CopyExtendedMemory), () => Handler.CopyExtendedMemory(es, si, cx), result => result.carryFlag);
    }

    /// <summary>
    /// INT 15h AH=88h: returns the extended memory size in KB in AX.
    /// </summary>
    public (ushort ax, bool carryFlag) GetExtendedMemorySize(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(GetExtendedMemorySize), () => Handler.GetExtendedMemorySize(), result => result.carryFlag);
    }

    /// <summary>
    /// INT 15h AH=90h: device busy notification; returns immediately.
    /// </summary>
    public (byte ah, bool carryFlag) DeviceBusy(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(DeviceBusy), () => Handler.DeviceBusy(), result => result.carryFlag);
    }

    /// <summary>
    /// INT 15h AH=91h: device post-complete notification; returns immediately.
    /// </summary>
    public (byte ah, bool carryFlag) DevicePost(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(DevicePost), () => Handler.DevicePost(), result => result.carryFlag);
    }

    /// <summary>
    /// INT 15h AH=C0h: unsupported function; reports the error in AH and CF.
    /// </summary>
    public (byte ah, bool carryFlag) UnsupportedC0(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(UnsupportedC0), () => Handler.Unsupported(), result => result.carryFlag);
    }

    /// <summary>
    /// INT 15h AH=C2h: unsupported function; reports the error in AH and CF.
    /// </summary>
    public (byte ah, bool carryFlag) UnsupportedC2(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(UnsupportedC2), () => Handler.Unsupported(), result => result.carryFlag);
    }

    /// <summary>
    /// INT 15h AH=C4h: unsupported function; reports the error in AH and CF.
    /// </summary>
    public (byte ah, bool carryFlag) UnsupportedC4(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(UnsupportedC4), () => Handler.Unsupported(), result => result.carryFlag);
    }
}
