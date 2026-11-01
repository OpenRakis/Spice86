namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 16h services.
/// </summary>
public sealed class KeyboardServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyboardServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public KeyboardServices(CSharpOverrideHelper helper) : base(helper, 0x16, ServiceSelector.Ah) {
    }

    /// <summary>
    /// INT 16h AH=01h: reports a pending keystroke in ZF and returns it in AX without dequeuing.
    /// </summary>
    public void GetKeystrokeStatus(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x01);
    }

    /// <summary>
    /// INT 16h AH=02h: returns the shift-flag byte in AL.
    /// </summary>
    public void GetShiftFlags(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x02);
    }

    /// <summary>
    /// INT 16h AH=03h: sets the typematic rate and delay (AL selects, BL=rate, BH=delay).
    /// </summary>
    public void SetTypematicRateAndDelay(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x03);
    }

    /// <summary>
    /// INT 16h AH=05h: stores the key code in CX into the BIOS keyboard buffer.
    /// </summary>
    public void StoreKeystroke(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx) {
        CheckVector();
        State.CX = cx;
        Call(expectedReturnCs, expectedReturnIp, 0x05);
    }

    /// <summary>
    /// INT 16h AH=11h: reports a pending keystroke in ZF and returns it in AX (enhanced keyboard).
    /// </summary>
    public void GetEnhancedKeystrokeStatus(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x11);
    }

    /// <summary>
    /// INT 16h AH=1Dh: unsupported function.
    /// </summary>
    public void Unsupported(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x1D);
    }
}
