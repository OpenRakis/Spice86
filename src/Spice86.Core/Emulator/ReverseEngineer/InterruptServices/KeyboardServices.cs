namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

/// <summary>
/// Typed helpers for the INT 16h services.
/// </summary>
public sealed class KeyboardServices : InterruptServiceGroup<KeyboardInt16Handler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyboardServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public KeyboardServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 16h AH=01h: reports a pending keystroke in ZF and returns it in AX without dequeuing.
    /// </summary>
    public (ushort ax, bool zeroFlag) GetKeystrokeStatus(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(GetKeystrokeStatus));
        return (State.AX, State.ZeroFlag);
    }

    /// <summary>
    /// INT 16h AH=02h: returns the shift-flag byte in AL.
    /// </summary>
    public byte GetShiftFlags(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(GetShiftFlags));
        return State.AL;
    }

    /// <summary>
    /// INT 16h AH=03h: sets the typematic rate and delay (AL selects, BL=rate, BH=delay).
    /// </summary>
    public void SetTypematicRateAndDelay(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, nameof(SetTypematicRateAndDelay));
    }

    /// <summary>
    /// INT 16h AH=05h: stores the key code in CX into the BIOS keyboard buffer.
    /// </summary>
    public (byte al, bool carryFlag) StoreKeystroke(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx) {
        CheckVector();
        State.CX = cx;
        Call(expectedReturnCs, expectedReturnIp, nameof(StoreKeystroke));
        return (State.AL, State.CarryFlag);
    }

    /// <summary>
    /// INT 16h AH=11h: reports a pending keystroke in ZF and returns it in AX (enhanced keyboard).
    /// </summary>
    public (ushort ax, bool zeroFlag) GetEnhancedKeystrokeStatus(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(GetEnhancedKeystrokeStatus));
        return (State.AX, State.ZeroFlag);
    }

    /// <summary>
    /// INT 16h AH=1Dh: unsupported function.
    /// </summary>
    public void Unsupported(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(Unsupported));
    }

    /// <summary>
    /// Sets the typematic rate and delay directly, without an interrupt frame.
    /// </summary>
    public void SetTypematicRateAndDelay(byte operation, ushort bx) {
        Handler.SetTypematicRateAndDelay(operation, bx);
    }

    /// <summary>
    /// Returns the BIOS keyboard shift-flag byte directly, without an interrupt frame.
    /// </summary>
    public byte ReadShiftFlags() {
        return Handler.GetShiftFlags();
    }

    /// <summary>
    /// Stores <paramref name="keyCode"/> in the BIOS keyboard buffer directly, without an interrupt frame.
    /// </summary>
    public (byte al, bool carryFlag) StoreKeystroke(ushort keyCode) {
        return Handler.StoreKeystroke(keyCode);
    }

    /// <summary>
    /// Returns the pending key code without dequeuing directly, without an interrupt frame.
    /// </summary>
    public (ushort? ax, bool? zeroFlag) ReadKeystrokeStatus() {
        return Handler.ReadKeystrokeStatus();
    }

    /// <summary>
    /// Logs and ignores an unsupported keyboard operation directly, without an interrupt frame.
    /// </summary>
    public void Unsupported(int operation) {
        Handler.Unsupported(operation);
    }
}
