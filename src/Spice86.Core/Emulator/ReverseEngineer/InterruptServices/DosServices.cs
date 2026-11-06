namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed helpers for the INT 21h services.
/// </summary>
public sealed class DosServices : InterruptServiceGroup<DosInt21Handler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="DosServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public DosServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 21h AH=02h: writes the character in DL to standard output.
    /// </summary>
    public byte DisplayOutput(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, nameof(DisplayOutput));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=03h: reads a character from STDAUX into AL.
    /// </summary>
    public byte ReadCharacterFromStdAux(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(ReadCharacterFromStdAux));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=04h: writes the character in AL to STDAUX.
    /// </summary>
    public void WriteCharacterToStdAux(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, nameof(WriteCharacterToStdAux));
    }

    /// <summary>
    /// INT 21h AH=05h: writes the character in AL to the printer.
    /// </summary>
    public void PrinterOutput(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, nameof(PrinterOutput));
    }

    /// <summary>
    /// INT 21h AH=06h: direct console I/O; DL=FFh reads a character into AL, otherwise writes DL.
    /// </summary>
    public (byte al, bool zeroFlag) DirectConsoleIo(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, nameof(DirectConsoleIo));
        return (State.AL, State.ZeroFlag);
    }

    /// <summary>
    /// INT 21h AH=09h: writes the $-terminated string at DS:DX to standard output.
    /// </summary>
    public void PrintString(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(PrintString));
    }

    /// <summary>
    /// INT 21h AH=09h: writes the $-terminated string at DS:DX to standard output, when the
    /// string is the constant <paramref name="text"/>.
    /// </summary>
    public void PrintString(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, (byte)'$');
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(PrintString));
    }

    /// <summary>
    /// INT 21h AH=0Bh: returns in AL whether a character is waiting on standard input.
    /// </summary>
    public byte CheckStandardInputStatus(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(CheckStandardInputStatus));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=0Dh: flushes the disk buffers.
    /// </summary>
    public void DiskReset(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(DiskReset));
    }

    /// <summary>
    /// INT 21h AH=0Eh: makes DL the default drive and returns the drive count in AL.
    /// </summary>
    public byte SelectDefaultDrive(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, nameof(SelectDefaultDrive));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=0Fh: opens the file named by the FCB at DS:DX; status in AL.
    /// </summary>
    public byte FcbOpenFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbOpenFile));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=10h: closes the file of the FCB at DS:DX; status in AL.
    /// </summary>
    public byte FcbCloseFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbCloseFile));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=11h: finds the first file matching the FCB at DS:DX; status in AL.
    /// </summary>
    public byte FcbFindFirst(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbFindFirst));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=12h: finds the next file matching the FCB at DS:DX; status in AL.
    /// </summary>
    public byte FcbFindNext(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbFindNext));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=13h: deletes the files matching the FCB at DS:DX; status in AL.
    /// </summary>
    public byte FcbDeleteFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbDeleteFile));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=14h: reads the current record of the FCB at DS:DX into the DTA; status in AL.
    /// </summary>
    public byte FcbSequentialRead(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbSequentialRead));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=15h: writes the DTA to the current record of the FCB at DS:DX; status in AL.
    /// </summary>
    public byte FcbSequentialWrite(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbSequentialWrite));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=16h: creates the file named by the FCB at DS:DX; status in AL.
    /// </summary>
    public byte FcbCreateFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbCreateFile));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=17h: renames the file of the extended FCB at DS:DX; status in AL.
    /// </summary>
    public byte FcbRenameFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbRenameFile));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=19h: returns the default drive in AL.
    /// </summary>
    public byte GetCurrentDefaultDrive(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(GetCurrentDefaultDrive));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=1Ah: sets the disk transfer address to DS:DX.
    /// </summary>
    public void SetDiskTransferAddress(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(SetDiskTransferAddress));
    }

    /// <summary>
    /// INT 21h AH=1Bh: returns allocation information of the default drive.
    /// </summary>
    public (ushort ax, ushort cx, ushort dx, ushort bx, ushort ds) GetAllocationInfoForDefaultDrive(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(GetAllocationInfoForDefaultDrive));
        return (State.AX, State.CX, State.DX, State.BX, State.DS);
    }

    /// <summary>
    /// INT 21h AH=1Ch: returns allocation information of drive DL.
    /// </summary>
    public (ushort ax, ushort cx, ushort dx, ushort bx, ushort ds) GetAllocationInfoForAnyDrive(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, nameof(GetAllocationInfoForAnyDrive));
        return (State.AX, State.CX, State.DX, State.BX, State.DS);
    }

    /// <summary>
    /// INT 21h AH=21h: reads the random record of the FCB at DS:DX into the DTA; status in AL.
    /// </summary>
    public byte FcbRandomRead(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbRandomRead));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=22h: writes the DTA to the random record of the FCB at DS:DX; status in AL.
    /// </summary>
    public byte FcbRandomWrite(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbRandomWrite));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=23h: writes the file size of the FCB at DS:DX into its random record field; status in AL.
    /// </summary>
    public byte FcbGetFileSize(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbGetFileSize));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=24h: sets the random record field of the FCB at DS:DX from its current block and record.
    /// </summary>
    public void FcbSetRandomRecordNumber(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbSetRandomRecordNumber));
    }

    /// <summary>
    /// INT 21h AH=25h: sets interrupt vector AL to the address in DS:DX.
    /// </summary>
    public void SetInterruptVector(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(SetInterruptVector));
    }

    /// <summary>
    /// INT 21h AH=26h: creates a new PSP at segment DX.
    /// </summary>
    public byte CreateNewPsp(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx) {
        CheckVector();
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(CreateNewPsp));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=27h: reads CX records from the random record of the FCB at DS:DX into the DTA.
    /// </summary>
    public (byte al, ushort cx) FcbRandomBlockRead(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbRandomBlockRead));
        return (State.AL, State.CX);
    }

    /// <summary>
    /// INT 21h AH=28h: writes CX records from the DTA to the random record of the FCB at DS:DX.
    /// </summary>
    public (byte al, ushort cx) FcbRandomBlockWrite(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbRandomBlockWrite));
        return (State.AL, State.CX);
    }

    /// <summary>
    /// INT 21h AH=29h: parses the file name at DS:SI into the FCB at ES:DI.
    /// </summary>
    public (byte al, ushort si) FcbParseFilename(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort si, ushort di, ushort es, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.ES = es;
        State.AL = al;
        State.SI = si;
        State.DI = di;
        Call(expectedReturnCs, expectedReturnIp, nameof(FcbParseFilename));
        return (State.AL, State.SI);
    }

    /// <summary>
    /// INT 21h AH=2Ah: returns the date in CX and DX and the day of week in AL.
    /// </summary>
    public (ushort ax, ushort cx, ushort dx) GetDate(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(GetDate));
        return (State.AX, State.CX, State.DX);
    }

    /// <summary>
    /// INT 21h AH=2Bh: sets the date from CX and DX; status in AL.
    /// </summary>
    public byte SetDate(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CheckVector();
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(SetDate));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=2Ch: returns the time in CX and DX.
    /// </summary>
    public (ushort cx, ushort dx) GetTime(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(GetTime));
        return (State.CX, State.DX);
    }

    /// <summary>
    /// INT 21h AH=2Dh: sets the time from CX and DX; status in AL.
    /// </summary>
    public byte SetTime(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CheckVector();
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(SetTime));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=2Fh: returns the disk transfer address in ES:BX.
    /// </summary>
    public (ushort bx, ushort es) GetDiskTransferAddress(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(GetDiskTransferAddress));
        return (State.BX, State.ES);
    }

    /// <summary>
    /// INT 21h AH=30h: returns the DOS version in AX, BX and CX.
    /// </summary>
    public (ushort ax, ushort cx, ushort bx) GetDosVersion(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(GetDosVersion));
        return (State.AX, State.CX, State.BX);
    }

    /// <summary>
    /// INT 21h AH=33h: gets or sets the Ctrl-Break flag (AL selects, DL sets).
    /// </summary>
    public (byte al, ushort dx, ushort bx) GetSetControlBreak(ushort expectedReturnCs, ushort expectedReturnIp, byte al, byte dl) {
        CheckVector();
        State.AL = al;
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, nameof(GetSetControlBreak));
        return (State.AL, State.DX, State.BX);
    }

    /// <summary>
    /// INT 21h AH=34h: returns the address of the InDOS flag in ES:BX.
    /// </summary>
    public (ushort bx, ushort es) GetInDosFlagAddress(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(GetInDosFlagAddress));
        return (State.BX, State.ES);
    }

    /// <summary>
    /// INT 21h AH=35h: returns the address of interrupt vector AL in ES:BX.
    /// </summary>
    public (ushort bx, ushort es) GetInterruptVector(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, nameof(GetInterruptVector));
        return (State.BX, State.ES);
    }

    /// <summary>
    /// INT 21h AH=36h: returns the free space of drive DL in AX, BX, CX and DX.
    /// </summary>
    public (ushort ax, ushort cx, ushort dx, ushort bx) GetFreeDiskSpace(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, nameof(GetFreeDiskSpace));
        return (State.AX, State.CX, State.DX, State.BX);
    }

    /// <summary>
    /// INT 21h AH=38h: gets or sets country information (AL, BX) via the buffer at DS:DX.
    /// </summary>
    public (ushort ax, bool carryFlag) SetCountryCode(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort bx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.AL = al;
        State.BX = bx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(SetCountryCode));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=39h: creates the directory at DS:DX; error code in AX with CF set on failure.
    /// </summary>
    public (ushort ax, bool carryFlag) CreateDirectory(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(CreateDirectory));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=39h: creates the directory at DS:DX, when its name is the constant
    /// <paramref name="text"/>; error code in AX with CF set on failure.
    /// </summary>
    public (ushort ax, bool carryFlag) CreateDirectory(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(CreateDirectory));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=3Ah: removes the directory at DS:DX; error code in AX with CF set on failure.
    /// </summary>
    public (ushort ax, bool carryFlag) RemoveDirectory(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(RemoveDirectory));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=3Ah: removes the directory at DS:DX, when its name is the constant
    /// <paramref name="text"/>; error code in AX with CF set on failure.
    /// </summary>
    public (ushort ax, bool carryFlag) RemoveDirectory(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(RemoveDirectory));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=3Bh: changes the current directory to DS:DX; error code in AX with CF set on failure.
    /// </summary>
    public (ushort ax, bool carryFlag) ChangeCurrentDirectory(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(ChangeCurrentDirectory));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=3Bh: changes the current directory to DS:DX, when its name is the constant
    /// <paramref name="text"/>; error code in AX with CF set on failure.
    /// </summary>
    public (ushort ax, bool carryFlag) ChangeCurrentDirectory(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(ChangeCurrentDirectory));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=3Ch: creates or truncates the file at DS:DX with attributes CX; handle in AX.
    /// </summary>
    public (ushort ax, bool carryFlag) CreateFileUsingHandle(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(CreateFileUsingHandle));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=3Ch: creates or truncates the file at DS:DX with attributes CX, when its name is
    /// the constant <paramref name="text"/>; handle in AX.
    /// </summary>
    public (ushort ax, bool carryFlag) CreateFileUsingHandle(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(CreateFileUsingHandle));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=3Dh: opens the file or device at DS:DX with access mode AL; handle in AX.
    /// </summary>
    public (ushort ax, bool carryFlag) OpenFileOrDevice(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(OpenFileOrDevice));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=3Dh: opens the file or device at DS:DX with access mode AL, when its name is the
    /// constant <paramref name="text"/>; handle in AX.
    /// </summary>
    public (ushort ax, bool carryFlag) OpenFileOrDevice(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(OpenFileOrDevice));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=3Eh: closes handle BX; error code in AX with CF set on failure.
    /// </summary>
    public (ushort ax, bool carryFlag) CloseFileOrDevice(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, nameof(CloseFileOrDevice));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=3Fh: reads CX bytes of handle BX into DS:DX; bytes read in AX.
    /// </summary>
    public (ushort ax, bool carryFlag) ReadFileOrDevice(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort bx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(ReadFileOrDevice));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=40h: writes CX bytes from DS:DX to handle BX; bytes written in AX.
    /// </summary>
    public (ushort ax, bool carryFlag) WriteToFileOrDevice(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort bx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(WriteToFileOrDevice));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=41h: deletes the file at DS:DX; error code in AX with CF set on failure.
    /// </summary>
    public (ushort ax, bool carryFlag) RemoveFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(RemoveFile));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=41h: deletes the file at DS:DX, when its name is the constant
    /// <paramref name="text"/>; error code in AX with CF set on failure.
    /// </summary>
    public (ushort ax, bool carryFlag) RemoveFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(RemoveFile));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=42h: moves the pointer of handle BX by CX:DX (mode AL); new position in DX:AX.
    /// </summary>
    public (ushort ax, ushort dx, bool carryFlag) MoveFilePointerUsingHandle(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(MoveFilePointerUsingHandle));
        return (State.AX, State.DX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=43h: gets or sets the attributes of the file at DS:DX (AL selects) in CX.
    /// </summary>
    public (ushort ax, ushort cx, bool carryFlag) GetSetFileAttributes(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.AL = al;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(GetSetFileAttributes));
        return (State.AX, State.CX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=43h: gets or sets the attributes of the file at DS:DX (AL selects) in CX, when
    /// its name is the constant <paramref name="text"/>.
    /// </summary>
    public (ushort ax, ushort cx, bool carryFlag) GetSetFileAttributes(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.AL = al;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(GetSetFileAttributes));
        return (State.AX, State.CX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=44h: performs the I/O-control subfunction AL on handle BX or drive BL; result in AX and DX.
    /// </summary>
    public (ushort ax, ushort dx, bool carryFlag) IoControl(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(IoControl));
        return (State.AX, State.DX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=45h: duplicates handle BX; new handle in AX.
    /// </summary>
    public (ushort ax, bool carryFlag) DuplicateFileHandle(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, nameof(DuplicateFileHandle));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=46h: forces handle CX to refer to the file of handle BX.
    /// </summary>
    public (ushort ax, bool carryFlag) ForceDuplicateFileHandle(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort bx) {
        CheckVector();
        State.BX = bx;
        State.CX = cx;
        Call(expectedReturnCs, expectedReturnIp, nameof(ForceDuplicateFileHandle));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=47h: writes the current directory of drive DL to the buffer at DS:SI.
    /// </summary>
    public (ushort ax, bool carryFlag) GetCurrentDirectory(ushort expectedReturnCs, ushort expectedReturnIp, byte dl, ushort si, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DL = dl;
        State.SI = si;
        Call(expectedReturnCs, expectedReturnIp, nameof(GetCurrentDirectory));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=48h: allocates a memory block of BX paragraphs; segment in AX.
    /// </summary>
    public (ushort ax, ushort bx, bool carryFlag) AllocateMemoryBlock(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, nameof(AllocateMemoryBlock));
        return (State.AX, State.BX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=49h: frees the memory block at segment ES.
    /// </summary>
    public (ushort ax, bool carryFlag) FreeMemoryBlock(ushort expectedReturnCs, ushort expectedReturnIp, ushort es) {
        CheckVector();
        State.ES = es;
        Call(expectedReturnCs, expectedReturnIp, nameof(FreeMemoryBlock));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=4Ah: resizes the memory block at segment ES to BX paragraphs.
    /// </summary>
    public (ushort ax, ushort bx, bool carryFlag) ModifyMemoryBlock(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx, ushort es) {
        CheckVector();
        State.ES = es;
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, nameof(ModifyMemoryBlock));
        return (State.AX, State.BX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=4Dh: returns the exit code of the last child process in AX.
    /// </summary>
    public ushort GetReturnCode(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(GetReturnCode));
        return State.AX;
    }

    /// <summary>
    /// INT 21h AH=4Eh: finds the first file matching the pattern at DS:DX with attributes CX.
    /// </summary>
    public (ushort ax, bool carryFlag) FindFirstMatchingFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FindFirstMatchingFile));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=4Eh: finds the first file matching the pattern at DS:DX with attributes CX, when
    /// the pattern is the constant <paramref name="text"/>.
    /// </summary>
    public (ushort ax, bool carryFlag) FindFirstMatchingFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(FindFirstMatchingFile));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=4Fh: finds the next file matching a previous find-first.
    /// </summary>
    public (ushort ax, bool carryFlag) FindNextMatchingFile(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(FindNextMatchingFile));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=50h: sets the current PSP segment to BX.
    /// </summary>
    public void SetCurrentPsp(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, nameof(SetCurrentPsp));
    }

    /// <summary>
    /// INT 21h AH=51h: returns the current PSP segment in BX.
    /// </summary>
    public ushort GetCurrentPsp(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(GetCurrentPsp));
        return State.BX;
    }

    /// <summary>
    /// INT 21h AH=52h: returns the DOS list of lists in ES:BX.
    /// </summary>
    public (ushort bx, ushort es) GetListOfLists(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(GetListOfLists));
        return (State.BX, State.ES);
    }

    /// <summary>
    /// INT 21h AH=55h: creates a child PSP at segment DX with the memory size in SI.
    /// </summary>
    public byte CreateChildPsp(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort si) {
        CheckVector();
        State.DX = dx;
        State.SI = si;
        Call(expectedReturnCs, expectedReturnIp, nameof(CreateChildPsp));
        return State.AL;
    }

    /// <summary>
    /// INT 21h AH=57h: gets or sets the date and time of the file at DS:DX (AL selects) in CX and DX.
    /// </summary>
    public (ushort ax, ushort cx, ushort dx, bool carryFlag) GetSetFileDateAndTime(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.AL = al;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(GetSetFileDateAndTime));
        return (State.AX, State.CX, State.DX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=58h: gets or sets the allocation strategy or UMB link state (AL selects, BX sets).
    /// </summary>
    public (ushort ax, bool carryFlag) AllocationStrategyOrUpperMemoryLinkState(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, nameof(AllocationStrategyOrUpperMemoryLinkState));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=62h: returns the current PSP segment in BX.
    /// </summary>
    public ushort GetPspAddress(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, nameof(GetPspAddress));
        return State.BX;
    }

    /// <summary>
    /// INT 21h AH=63h: returns the DBCS lead-byte table (AL selects).
    /// </summary>
    public (byte al, ushort si, ushort ds, bool carryFlag) GetLeadByteTable(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, nameof(GetLeadByteTable));
        return (State.AL, State.SI, State.DS, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=66h: gets or sets the global code page table (AL selects).
    /// </summary>
    public (ushort ax, ushort dx, ushort bx, bool carryFlag) GetSetGlobalLoadedCodePageTable(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, nameof(GetSetGlobalLoadedCodePageTable));
        return (State.AX, State.DX, State.BX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=67h: sets the maximum file handle count to BX.
    /// </summary>
    public (ushort ax, bool carryFlag) SetHandleCount(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, nameof(SetHandleCount));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>
    /// INT 21h AH=68h: commits buffered data for the file handle in BX; returns AX and CF.
    /// </summary>
    public (ushort ax, bool carryFlag) CommitFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, nameof(CommitFile));
        return (State.AX, State.CarryFlag);
    }

    /// <summary>Commits buffered data for the DOS file <paramref name="fileHandle"/>.</summary>
    public DosFileOperationResult CommitFile(ushort fileHandle) => Handler.CommitFile(fileHandle);

    /// <summary>Opens the file <paramref name="fileName"/> with the raw
    /// <paramref name="accessMode"/> byte; the result value is the DOS handle.</summary>
    public DosFileOperationResult OpenFile(byte accessMode, string fileName) =>
        Handler.OpenFile(accessMode, fileName);

    /// <summary>Creates the file <paramref name="fileName"/> with
    /// <paramref name="fileAttribute"/>; the result value is the DOS handle.</summary>
    public DosFileOperationResult CreateFile(string fileName, ushort fileAttribute) =>
        Handler.CreateFile(fileName, fileAttribute);

    /// <summary>Closes the DOS file <paramref name="fileHandle"/>.</summary>
    public DosFileOperationResult CloseFile(ushort fileHandle) =>
        Handler.CloseFile(fileHandle);

    /// <summary>Reads <paramref name="readLength"/> bytes from <paramref name="fileHandle"/>
    /// into the buffer at <paramref name="segment"/>:<paramref name="offset"/>.</summary>
    public DosFileOperationResult ReadFile(ushort fileHandle, ushort readLength,
        ushort segment, ushort offset) =>
        Handler.ReadFile(fileHandle, readLength, segment, offset);

    /// <summary>Writes <paramref name="writeLength"/> bytes to <paramref name="fileHandle"/>
    /// from the buffer at <paramref name="segment"/>:<paramref name="offset"/>.</summary>
    public DosFileOperationResult WriteFile(ushort fileHandle, ushort writeLength,
        ushort segment, ushort offset) =>
        Handler.WriteFile(fileHandle, writeLength, segment, offset);

    /// <summary>Moves the file pointer of <paramref name="fileHandle"/> by the signed
    /// <paramref name="offset"/> from <paramref name="originOfMove"/>.</summary>
    public DosFileOperationResult SeekFile(SeekOrigin originOfMove, ushort fileHandle, int offset) =>
        Handler.SeekFile(originOfMove, fileHandle, offset);

    /// <summary>Duplicates the DOS file <paramref name="fileHandle"/>; the result value is
    /// the new handle.</summary>
    public DosFileOperationResult DuplicateHandle(ushort fileHandle) =>
        Handler.DuplicateHandle(fileHandle);

    /// <summary>Duplicates the DOS file <paramref name="fileHandle"/> onto
    /// <paramref name="newHandle"/>.</summary>
    public DosFileOperationResult ForceDuplicateHandle(ushort fileHandle, ushort newHandle) =>
        Handler.ForceDuplicateHandle(fileHandle, newHandle);

    /// <summary>Creates the directory <paramref name="path"/>.</summary>
    public DosFileOperationResult CreateDirectory(string path) =>
        Handler.CreateDirectory(path);

    /// <summary>Removes the directory <paramref name="path"/>.</summary>
    public DosFileOperationResult RemoveDirectory(string path) =>
        Handler.RemoveDirectory(path);

    /// <summary>Deletes the file <paramref name="path"/>.</summary>
    public DosFileOperationResult DeleteFile(string path) =>
        Handler.DeleteFile(path);

    /// <summary>Sets the current DOS directory to <paramref name="newDirectory"/>.</summary>
    public DosFileOperationResult ChangeDirectory(string newDirectory) =>
        Handler.ChangeDirectory(newDirectory);

    /// <summary>Finds the first file matching <paramref name="fileSpec"/> with
    /// <paramref name="attributes"/>.</summary>
    public DosFileOperationResult FindFirst(string fileSpec, ushort attributes) =>
        Handler.FindFirst(fileSpec, attributes);

    /// <summary>Finds the next file matching a previous find-first.</summary>
    public DosFileOperationResult FindNext() =>
        Handler.FindNext();

    /// <summary>Resizes the current PSP's job file table to
    /// <paramref name="requestedSize"/> handles.</summary>
    public DosFileOperationResult ResizeHandleTable(ushort requestedSize) =>
        Handler.ResizeHandleTable(requestedSize);

    /// <summary>Frees the memory block whose segment is
    /// <paramref name="blockSegment"/>.</summary>
    public DosFileOperationResult FreeMemory(ushort blockSegment) =>
        Handler.FreeMemory(blockSegment);

    /// <summary>Writes <paramref name="text"/> to the standard output device.</summary>
    public void Print(string text) =>
        Handler.WriteString(text);

    /// <summary>Flushes the disk buffers.</summary>
    public void ResetDisk() => Handler.DiskReset();

    /// <summary>The file control block manager backing the FCB file services.</summary>
    public DosFcbManager FileControlBlocks => Handler.FileControlBlocks;

    /// <summary>Returns the FAT allocation information for the default drive.</summary>
    public (byte al, byte? ah, ushort? cx, ushort? dx, ushort? bx, ushort? ds) ReadAllocationInfoForDefaultDrive() =>
        Handler.ReadAllocationInfo(0);

    /// <summary>Returns the FAT allocation information for <paramref name="driveRequest"/>
    /// (0 = default, 1 = A:, ...).</summary>
    public (byte al, byte? ah, ushort? cx, ushort? dx, ushort? bx, ushort? ds) ReadAllocationInfoForAnyDrive(byte driveRequest) =>
        Handler.ReadAllocationInfo(driveRequest);

    /// <summary>Reads one character from the standard auxiliary device.</summary>
    public byte ReadAuxiliaryCharacter() => Handler.ReadAuxiliaryCharacter();

    /// <summary>Writes <paramref name="character"/> to the standard auxiliary device.</summary>
    public void WriteAuxiliaryCharacter(byte character) => Handler.WriteAuxiliaryCharacter(character);

    /// <summary>Writes <paramref name="character"/> to the printer device.</summary>
    public void WritePrinterCharacter(byte character) => Handler.WritePrinterCharacter(character);

    /// <summary>Returns 0xFF when a character is available on standard input, 0 otherwise.</summary>
    public byte ReadStandardInputStatus() => Handler.ReadStandardInputStatus();

    /// <summary>Writes <paramref name="character"/> to the standard output device and returns it.</summary>
    public byte WriteDisplayCharacter(byte character) => Handler.WriteDisplayCharacter(character);

    /// <summary>Returns the DOS date (day of week, year, month, day) from the CMOS registers.</summary>
    public (byte dayOfWeek, ushort year, byte month, byte day) ReadDate() =>
        Handler.ReadDate();

    /// <summary>Sets the DOS date; returns 0 on success or 0xFF on an invalid date.</summary>
    public byte SetDate(ushort year, byte month, byte day) =>
        Handler.SetDate(year, month, day);

    /// <summary>Returns the DOS time (hour, minute, second, hundredths) from the emulated clock.</summary>
    public (byte hour, byte minute, byte second, byte hundredths) ReadTime() =>
        Handler.ReadTime();

    /// <summary>Sets the DOS time; returns 0 on success or 0xFF on an invalid time.</summary>
    public byte SetTime(byte hour, byte minute, byte second, byte hundredths) =>
        Handler.SetTime(hour, minute, second, hundredths);

    /// <summary>Returns the Disk Transfer Area address.</summary>
    public SegmentedAddress ReadDiskTransferAddress() => Handler.ReadDiskTransferAddress();

    /// <summary>Sets the Disk Transfer Area address.</summary>
    public void SetDiskTransferAddress(ushort segment, ushort offset) =>
        Handler.SetDiskTransferAddress(segment, offset);

    /// <summary>Returns the reported DOS version: AX = 5.0, CX = 0, BX = 0xFF00.</summary>
    public (ushort ax, ushort cx, ushort bx) ReadDosVersion() => Handler.ReadDosVersion();

    /// <summary>Returns the reported free disk space for <paramref name="driveNumber"/>.</summary>
    public (ushort ax, ushort cx, ushort dx, ushort bx) ReadFreeDiskSpace(byte driveNumber) =>
        Handler.ReadFreeDiskSpace(driveNumber);

    /// <summary>Returns the address stored in the interrupt vector table for
    /// <paramref name="vectorNumber"/>.</summary>
    public SegmentedAddress ReadInterruptVector(byte vectorNumber) =>
        Handler.ReadInterruptVector(vectorNumber);

    /// <summary>Sets a new interrupt vector in the Interrupt Vector Table.</summary>
    public void SetInterruptVector(byte vectorNumber, ushort segment, ushort offset) =>
        Handler.SetInterruptVector(vectorNumber, segment, offset);

    /// <summary>Returns the segment of the current Program Segment Prefix.</summary>
    public ushort ReadPspAddress() => Handler.ReadPspAddress();

    /// <summary>Sets the current Program Segment Prefix segment.</summary>
    public void SetCurrentPsp(ushort segment) => Handler.SetCurrentPsp(segment);

    /// <summary>Creates a copy of the current PSP at <paramref name="segment"/>.</summary>
    public byte CreateNewPsp(ushort segment) => Handler.CreateNewPsp(segment);

    /// <summary>Creates a child PSP at <paramref name="segment"/> with the size
    /// <paramref name="sizeInParagraphs"/> and makes it the current PSP.</summary>
    public byte CreateChildPsp(ushort segment, ushort sizeInParagraphs) =>
        Handler.CreateChildPsp(segment, sizeInParagraphs);

    /// <summary>Returns the address of the DOS internal tables ("List of Lists" / SYSVARS).</summary>
    public SegmentedAddress ReadListOfListsAddress() => Handler.ReadListOfListsAddress();

    /// <summary>Returns the address of the InDOS flag in the DOS Swappable Data Area.</summary>
    public SegmentedAddress ReadInDosFlagAddress() => Handler.ReadInDosFlagAddress();

    /// <summary>Returns the exit code of the last child process and clears the stored code.</summary>
    public ushort ReadReturnCode() => Handler.ReadReturnCode();

    /// <summary>Makes <paramref name="driveIndex"/> the default drive and returns the number of
    /// potentially valid drive letters.</summary>
    public byte SelectDefaultDrive(byte driveIndex) => Handler.SelectDefaultDrive(driveIndex);

    /// <summary>Returns the current default drive index (0 = A:, 1 = B:, ...).</summary>
    public byte ReadCurrentDefaultDrive() => Handler.ReadCurrentDefaultDrive();

    /// <summary>Reads a character from standard input when <paramref name="character"/> is 0xFF
    /// (returned with the zero flag), otherwise writes it to standard output.</summary>
    public (byte? al, bool? zeroFlag) DirectConsoleIo(byte character) =>
        Handler.DirectConsoleIo(character);

    /// <summary>Returns the DBCS lead-byte table for <paramref name="operation"/> 0.</summary>
    public (byte al, SegmentedAddress? address, bool? carryFlag) ReadLeadByteTable(byte operation) =>
        Handler.ReadLeadByteTable(operation);

    /// <summary>Queries or sets the global code page (see
    /// <see cref="DosInt21Handler.GetSetCodePage"/>).</summary>
    public (DosFileOperationResult result, ushort bx, ushort dx) GetSetCodePage(
        byte operation, ushort activeCodePage, ushort systemCodePage) =>
        Handler.GetSetCodePage(operation, activeCodePage, systemCodePage);

    /// <summary>Gets or sets the Ctrl-C flag and related DOS variables.</summary>
    public (byte? al, byte? dh, byte? dl, ushort? bx) GetSetControlBreak(byte operation, byte value) =>
        Handler.GetSetControlBreak(operation, value);

    /// <summary>Writes the country information to the buffer at
    /// <paramref name="ds"/>:<paramref name="dx"/>.</summary>
    public DosFileOperationResult CountryInformation(byte operation, ushort bx, ushort ds, ushort dx) =>
        Handler.CountryInformation(operation, bx, ds, dx);

    /// <summary>Allocates a memory block of <paramref name="paragraphs"/> paragraphs.</summary>
    public (DosFileOperationResult result, ushort? largestFreeBlock) AllocateMemory(ushort paragraphs) =>
        Handler.AllocateMemory(paragraphs);

    /// <summary>Resizes the memory block at <paramref name="blockSegment"/> to
    /// <paramref name="paragraphs"/> paragraphs.</summary>
    public (DosFileOperationResult result, ushort? largestFreeBlock) ModifyMemory(
        ushort blockSegment, ushort paragraphs) =>
        Handler.ModifyMemory(blockSegment, paragraphs);

    /// <summary>Queries or sets the memory allocation strategy or the upper-memory link state.</summary>
    public (DosFileOperationResult result, byte? al) MemoryAllocationStrategy(
        byte operation, ushort value) =>
        Handler.MemoryAllocationStrategy(operation, value);

    /// <summary>Writes the ASCIIZ current directory of <paramref name="driveNumber"/> to the buffer
    /// at <paramref name="segment"/>:<paramref name="offset"/>.</summary>
    public DosFileOperationResult GetCurrentDirectory(byte driveNumber, ushort segment, ushort offset) =>
        Handler.GetCurrentDirectory(driveNumber, segment, offset);

    /// <summary>Gets or sets the attributes of the file <paramref name="dosFileName"/>.</summary>
    public (DosFileOperationResult result, ushort? attributes) GetSetFileAttributes(
        byte operation, string dosFileName, ushort attributes) =>
        Handler.GetSetFileAttributes(operation, dosFileName, attributes);

    /// <summary>Gets or sets the last-access date and time of the file
    /// <paramref name="dosFileName"/>.</summary>
    public (DosFileOperationResult result, ushort? time, ushort? date) GetSetFileDateAndTime(
        byte operation, string dosFileName, ushort dosFileTime, ushort dosFileDate) =>
        Handler.GetSetFileDateAndTime(operation, dosFileName, dosFileTime, dosFileDate);

    /// <summary>Performs an IOCTL subfunction; returns the result, the optional <c>AL</c> output
    /// and the <c>DX</c> output.</summary>
    public (DosFileOperationResult result, byte? al, ushort dx) IoControl(
        byte operation, ushort cx, ushort dx, ushort bx, ushort ds) =>
        Handler.IoControl(operation, cx, dx, bx, ds);
}
