namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 13h services.
/// </summary>
public sealed class DiskServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="DiskServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public DiskServices(CSharpOverrideHelper helper) : base(helper, 0x13, ServiceSelector.Ah) {
    }

    /// <summary>
    /// INT 13h AH=00h: resets the disk system of drive DL; status in AH with CF.
    /// </summary>
    public void ResetDiskSystem(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x00);
    }

    /// <summary>
    /// INT 13h AH=01h: returns the status of the last disk operation on drive DL in AH.
    /// </summary>
    public void GetLastStatus(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x01);
    }

    /// <summary>
    /// INT 13h AH=02h: reads AL sectors at CHS CX/DX into the buffer at ES:BX; status in AH with CF.
    /// </summary>
    public void ReadSectors(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx, ushort es) {
        CheckVector();
        State.ES = es;
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x02);
    }

    /// <summary>
    /// INT 13h AH=03h: writes AL sectors from the buffer at ES:BX to CHS CX/DX; status in AH with CF.
    /// </summary>
    public void WriteSectors(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx, ushort es) {
        CheckVector();
        State.ES = es;
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x03);
    }

    /// <summary>
    /// INT 13h AH=04h: verifies AL sectors at CHS CX/DX; status in AH with CF.
    /// </summary>
    public void VerifySectors(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx) {
        CheckVector();
        State.AL = al;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x04);
    }

    /// <summary>
    /// INT 13h AH=08h: returns the geometry of drive DL in CX, DH and DL; status in AH with CF.
    /// </summary>
    public void GetDriveParameters(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x08);
    }

    /// <summary>
    /// INT 13h AH=0Ch: seeks drive DH/DL to the cylinder in CX; status in AH with CF.
    /// </summary>
    public void SeekToCylinder(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CheckVector();
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x0C);
    }

    /// <summary>
    /// INT 13h AH=0Dh: resets the hard disk controller of drive DL; status in AH with CF.
    /// </summary>
    public void ResetHardDiskController(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x0D);
    }

    /// <summary>
    /// INT 13h AH=10h: checks whether drive DL is ready; status in AH with CF.
    /// </summary>
    public void TestDriveReady(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x10);
    }

    /// <summary>
    /// INT 13h AH=11h: recalibrates drive DL; status in AH with CF.
    /// </summary>
    public void Recalibrate(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x11);
    }

    /// <summary>
    /// INT 13h AH=15h: returns the type of drive DL in AH.
    /// </summary>
    public void GetDisketteOrHddType(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x15);
    }

    /// <summary>
    /// INT 13h AH=16h: returns the disk-change line status of drive DL in AH.
    /// </summary>
    public void GetDiskChangeLineStatus(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x16);
    }

    /// <summary>
    /// INT 13h AH=17h: sets the DASD type for format of drive DL from AL; status in AH with CF.
    /// </summary>
    public void HandleDasdTypeForFormat(ushort expectedReturnCs, ushort expectedReturnIp, byte al, byte dl) {
        CheckVector();
        State.AL = al;
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x17);
    }

    /// <summary>
    /// INT 13h AH=18h: returns the media type for format of drive DL (CHS in CX); status in AH with CF.
    /// </summary>
    public void HandleMediaTypeForFormat(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, byte dl) {
        CheckVector();
        State.AL = al;
        State.CX = cx;
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x18);
    }
}
