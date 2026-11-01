namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 1Ah services.
/// </summary>
public sealed class ClockServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="ClockServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public ClockServices(CSharpOverrideHelper helper) : base(helper, 0x1A, ServiceSelector.Ah) {
    }

    /// <summary>
    /// INT 1Ah AH=00h: returns the BIOS tick counter in CX:DX and the midnight flag in AL.
    /// </summary>
    public void GetSystemClockCounter(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x00);
    }

    /// <summary>
    /// INT 1Ah AH=01h: sets the BIOS tick counter to CX:DX.
    /// </summary>
    public void SetSystemClockCounter(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CheckVector();
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x01);
    }

    /// <summary>
    /// INT 1Ah AH=02h: returns the real-time clock time in CH, CL and DH (BCD).
    /// </summary>
    public void ReadTimeFromRTC(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x02);
    }

    /// <summary>
    /// INT 1Ah AH=03h: sets the real-time clock time from CH, CL and DH (BCD).
    /// </summary>
    public void SetRTCTime(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x03);
    }

    /// <summary>
    /// INT 1Ah AH=04h: returns the real-time clock date in CH, CL and DH (BCD).
    /// </summary>
    public void ReadDateFromRTC(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x04);
    }

    /// <summary>
    /// INT 1Ah AH=05h: sets the real-time clock date from CH, CL and DH (BCD).
    /// </summary>
    public void SetRTCDate(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x05);
    }
}
