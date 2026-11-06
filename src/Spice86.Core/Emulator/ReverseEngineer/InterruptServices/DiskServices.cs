namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Bios;

/// <summary>
/// Typed helpers for the INT 13h services.
/// </summary>
public sealed class DiskServices : InterruptServiceGroup<SystemBiosInt13Handler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="DiskServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public DiskServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 13h AH=00h: resets the disk system of drive DL; status in AH with CF.
    /// </summary>
    public bool ResetDiskSystem(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(ResetDiskSystem), () => Handler.ResetDiskSystem(dl), carryFlag => carryFlag);
    }

    /// <summary>
    /// INT 13h AH=01h: returns the status of the last disk operation on drive DL in AH.
    /// </summary>
    public (byte ah, bool carryFlag) GetLastStatus(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(GetLastStatus), () => Handler.GetLastStatus(dl), result => result.carryFlag);
    }

    /// <summary>
    /// INT 13h AH=02h: reads AL sectors at CHS CX/DX into the buffer at ES:BX; status in AH with CF.
    /// </summary>
    public (ushort ax, bool carryFlag) ReadSectors(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx, ushort es) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(ReadSectors), () => Handler.ReadSectors((byte)dx, al, es, bx, cx, dx), result => result.carryFlag);
    }

    /// <summary>
    /// INT 13h AH=03h: writes AL sectors from the buffer at ES:BX to CHS CX/DX; status in AH with CF.
    /// </summary>
    public (ushort ax, bool carryFlag) WriteSectors(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx, ushort es) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(WriteSectors), () => Handler.WriteSectors((byte)dx, al, es, bx, cx, dx), result => result.carryFlag);
    }

    /// <summary>
    /// INT 13h AH=04h: verifies AL sectors at CHS CX/DX; status in AH with CF.
    /// </summary>
    public (byte ah, bool carryFlag) VerifySectors(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(VerifySectors), () => Handler.VerifySectors(al, (byte)dx), result => result.carryFlag);
    }

    /// <summary>
    /// INT 13h AH=08h: returns the geometry of drive DL in CX, DH and DL; status in AH with CF.
    /// </summary>
    public (ushort ax, ushort cx, ushort dx, ushort bx, bool carryFlag) GetDriveParameters(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        (byte ah, byte? al, ushort? cx, ushort? dx, byte? bl, bool carryFlag) =
            CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
                nameof(GetDriveParameters), () => Handler.GetDriveParameters(dl), result => result.carryFlag);
        return ((ushort)((ah << 8) | (al ?? State.AL)), cx ?? State.CX,
            dx ?? (ushort)((State.DH << 8) | dl),
            (ushort)((State.BH << 8) | (bl ?? State.BL)), carryFlag);
    }

    /// <summary>
    /// INT 13h AH=0Ch: seeks drive DH/DL to the cylinder in CX; status in AH with CF.
    /// </summary>
    public (byte ah, bool carryFlag) SeekToCylinder(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(SeekToCylinder), () => Handler.SeekToCylinder((byte)dx), result => result.carryFlag);
    }

    /// <summary>
    /// INT 13h AH=0Dh: resets the hard disk controller of drive DL; status in AH with CF.
    /// </summary>
    public (byte ah, bool carryFlag) ResetHardDiskController(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        bool carryFlag = CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(ResetHardDiskController), () => Handler.ResetDiskSystem(dl), result => result);
        return ((byte)EntryOf(nameof(ResetHardDiskController)).SelectorValue, carryFlag);
    }

    /// <summary>
    /// INT 13h AH=10h: checks whether drive DL is ready; status in AH with CF.
    /// </summary>
    public (byte ah, bool carryFlag) TestDriveReady(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(TestDriveReady), () => Handler.TestDriveReady(dl), result => result.carryFlag);
    }

    /// <summary>
    /// INT 13h AH=11h: recalibrates drive DL; status in AH with CF.
    /// </summary>
    public (byte ah, bool carryFlag) Recalibrate(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(Recalibrate), () => Handler.Recalibrate(), result => result.carryFlag);
    }

    /// <summary>
    /// INT 13h AH=15h: returns the type of drive DL in AH.
    /// </summary>
    public (byte ah, ushort cx, ushort dx, bool carryFlag) GetDisketteOrHddType(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        (byte ah, ushort? cx, ushort? dx, bool carryFlag) =
            CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
                nameof(GetDisketteOrHddType), () => Handler.GetDisketteOrHddType(dl), result => result.carryFlag);
        return (ah, cx ?? State.CX, dx ?? (ushort)((State.DH << 8) | dl), carryFlag);
    }

    /// <summary>
    /// INT 13h AH=16h: returns the disk-change line status of drive DL in AH.
    /// </summary>
    public (byte ah, bool carryFlag) GetDiskChangeLineStatus(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(GetDiskChangeLineStatus), () => Handler.GetDiskChangeLineStatus(dl), result => result.carryFlag);
    }

    /// <summary>
    /// INT 13h AH=17h: sets the DASD type for format of drive DL from AL; status in AH with CF.
    /// </summary>
    public (byte ah, bool carryFlag) HandleDasdTypeForFormat(ushort expectedReturnCs, ushort expectedReturnIp, byte al, byte dl) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(HandleDasdTypeForFormat), () => Handler.HandleDasdTypeForFormat(), result => result.carryFlag);
    }

    /// <summary>
    /// INT 13h AH=18h: returns the media type for format of drive DL (CHS in CX); status in AH with CF.
    /// </summary>
    public (byte ah, bool carryFlag) HandleMediaTypeForFormat(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, byte dl) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(HandleMediaTypeForFormat), () => Handler.HandleMediaTypeForFormat(), result => result.carryFlag);
    }
}
