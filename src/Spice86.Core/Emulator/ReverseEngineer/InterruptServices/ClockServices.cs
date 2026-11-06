namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.SystemClock;

/// <summary>
/// Typed helpers for the INT 1Ah services.
/// </summary>
public sealed class ClockServices : InterruptServiceGroup<SystemClockInt1AHandler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="ClockServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public ClockServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 1Ah AH=00h: returns the BIOS tick counter in CX:DX and the midnight flag in AL.
    /// </summary>
    public (byte al, ushort cx, ushort dx) GetSystemClockCounter(ushort expectedReturnCs, ushort expectedReturnIp) =>
        CallDirect(expectedReturnCs, expectedReturnIp, nameof(GetSystemClockCounter),
            Handler.ReadSystemClockCounter);

    /// <summary>
    /// INT 1Ah AH=01h: sets the BIOS tick counter to CX:DX.
    /// </summary>
    public void SetSystemClockCounter(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) =>
        CallDirect(expectedReturnCs, expectedReturnIp, nameof(SetSystemClockCounter),
            () => Handler.SetSystemClockCounter(cx, dx));

    /// <summary>
    /// INT 1Ah AH=02h: returns the real-time clock time in CH, CL and DH (BCD).
    /// </summary>
    public (ushort cx, ushort dx, bool carryFlag) ReadTimeFromRTC(ushort expectedReturnCs, ushort expectedReturnIp) =>
        CallDirectWithCarry(expectedReturnCs, expectedReturnIp, nameof(ReadTimeFromRTC),
            Handler.ReadTimeFromRTC, result => result.carryFlag);

    /// <summary>
    /// INT 1Ah AH=03h: sets the real-time clock time from CH, CL and DH (BCD).
    /// </summary>
    public bool SetRTCTime(ushort expectedReturnCs, ushort expectedReturnIp) =>
        CallDirectWithCarry(expectedReturnCs, expectedReturnIp, nameof(SetRTCTime),
            Handler.SetRTCTime, carryFlag => carryFlag);

    /// <summary>
    /// INT 1Ah AH=04h: returns the real-time clock date in CH, CL and DH (BCD).
    /// </summary>
    public (ushort cx, ushort dx, bool carryFlag) ReadDateFromRTC(ushort expectedReturnCs, ushort expectedReturnIp) =>
        CallDirectWithCarry(expectedReturnCs, expectedReturnIp, nameof(ReadDateFromRTC),
            Handler.ReadDateFromRTC, result => result.carryFlag);

    /// <summary>
    /// INT 1Ah AH=05h: sets the real-time clock date from CH, CL and DH (BCD).
    /// </summary>
    public bool SetRTCDate(ushort expectedReturnCs, ushort expectedReturnIp) =>
        CallDirectWithCarry(expectedReturnCs, expectedReturnIp, nameof(SetRTCDate),
            Handler.SetRTCDate, carryFlag => carryFlag);
}
