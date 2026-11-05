namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 21h services.
/// </summary>
public sealed class DosServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="DosServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public DosServices(CSharpOverrideHelper helper) : base(helper, 0x21, ServiceSelector.Ah) {
    }

    /// <summary>
    /// INT 21h AH=02h: writes the character in DL to standard output.
    /// </summary>
    public void DisplayOutput(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x02);
    }

    /// <summary>
    /// INT 21h AH=03h: reads a character from STDAUX into AL.
    /// </summary>
    public void ReadCharacterFromStdAux(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x03);
    }

    /// <summary>
    /// INT 21h AH=04h: writes the character in AL to STDAUX.
    /// </summary>
    public void WriteCharacterToStdAux(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x04);
    }

    /// <summary>
    /// INT 21h AH=05h: writes the character in AL to the printer.
    /// </summary>
    public void PrinterOutput(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x05);
    }

    /// <summary>
    /// INT 21h AH=06h: direct console I/O; DL=FFh reads a character into AL, otherwise writes DL.
    /// </summary>
    public void DirectConsoleIo(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x06);
    }

    /// <summary>
    /// INT 21h AH=09h: writes the $-terminated string at DS:DX to standard output.
    /// </summary>
    public void PrintString(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x09);
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
        Call(expectedReturnCs, expectedReturnIp, 0x09);
    }

    /// <summary>
    /// INT 21h AH=0Bh: returns in AL whether a character is waiting on standard input.
    /// </summary>
    public void CheckStandardInputStatus(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x0B);
    }

    /// <summary>
    /// INT 21h AH=0Dh: flushes the disk buffers.
    /// </summary>
    public void DiskReset(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x0D);
    }

    /// <summary>
    /// INT 21h AH=0Eh: makes DL the default drive and returns the drive count in AL.
    /// </summary>
    public void SelectDefaultDrive(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x0E);
    }

    /// <summary>
    /// INT 21h AH=0Fh: opens the file named by the FCB at DS:DX; status in AL.
    /// </summary>
    public void FcbOpenFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x0F);
    }

    /// <summary>
    /// INT 21h AH=10h: closes the file of the FCB at DS:DX; status in AL.
    /// </summary>
    public void FcbCloseFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x10);
    }

    /// <summary>
    /// INT 21h AH=11h: finds the first file matching the FCB at DS:DX; status in AL.
    /// </summary>
    public void FcbFindFirst(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x11);
    }

    /// <summary>
    /// INT 21h AH=12h: finds the next file matching the FCB at DS:DX; status in AL.
    /// </summary>
    public void FcbFindNext(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x12);
    }

    /// <summary>
    /// INT 21h AH=13h: deletes the files matching the FCB at DS:DX; status in AL.
    /// </summary>
    public void FcbDeleteFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x13);
    }

    /// <summary>
    /// INT 21h AH=14h: reads the current record of the FCB at DS:DX into the DTA; status in AL.
    /// </summary>
    public void FcbSequentialRead(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x14);
    }

    /// <summary>
    /// INT 21h AH=15h: writes the DTA to the current record of the FCB at DS:DX; status in AL.
    /// </summary>
    public void FcbSequentialWrite(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x15);
    }

    /// <summary>
    /// INT 21h AH=16h: creates the file named by the FCB at DS:DX; status in AL.
    /// </summary>
    public void FcbCreateFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x16);
    }

    /// <summary>
    /// INT 21h AH=17h: renames the file of the extended FCB at DS:DX; status in AL.
    /// </summary>
    public void FcbRenameFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x17);
    }

    /// <summary>
    /// INT 21h AH=19h: returns the default drive in AL.
    /// </summary>
    public void GetCurrentDefaultDrive(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x19);
    }

    /// <summary>
    /// INT 21h AH=1Ah: sets the disk transfer address to DS:DX.
    /// </summary>
    public void SetDiskTransferAddress(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x1A);
    }

    /// <summary>
    /// INT 21h AH=1Bh: returns allocation information of the default drive.
    /// </summary>
    public void GetAllocationInfoForDefaultDrive(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x1B);
    }

    /// <summary>
    /// INT 21h AH=1Ch: returns allocation information of drive DL.
    /// </summary>
    public void GetAllocationInfoForAnyDrive(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x1C);
    }

    /// <summary>
    /// INT 21h AH=21h: reads the random record of the FCB at DS:DX into the DTA; status in AL.
    /// </summary>
    public void FcbRandomRead(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x21);
    }

    /// <summary>
    /// INT 21h AH=22h: writes the DTA to the random record of the FCB at DS:DX; status in AL.
    /// </summary>
    public void FcbRandomWrite(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x22);
    }

    /// <summary>
    /// INT 21h AH=23h: writes the file size of the FCB at DS:DX into its random record field; status in AL.
    /// </summary>
    public void FcbGetFileSize(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x23);
    }

    /// <summary>
    /// INT 21h AH=24h: sets the random record field of the FCB at DS:DX from its current block and record.
    /// </summary>
    public void FcbSetRandomRecordNumber(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x24);
    }

    /// <summary>
    /// INT 21h AH=25h: sets interrupt vector AL to the address in DS:DX.
    /// </summary>
    public void SetInterruptVector(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x25);
    }

    /// <summary>
    /// INT 21h AH=26h: creates a new PSP at segment DX.
    /// </summary>
    public void CreateNewPsp(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx) {
        CheckVector();
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x26);
    }

    /// <summary>
    /// INT 21h AH=27h: reads CX records from the random record of the FCB at DS:DX into the DTA.
    /// </summary>
    public void FcbRandomBlockRead(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x27);
    }

    /// <summary>
    /// INT 21h AH=28h: writes CX records from the DTA to the random record of the FCB at DS:DX.
    /// </summary>
    public void FcbRandomBlockWrite(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x28);
    }

    /// <summary>
    /// INT 21h AH=29h: parses the file name at DS:SI into the FCB at ES:DI.
    /// </summary>
    public void FcbParseFilename(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort si, ushort di, ushort es, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.ES = es;
        State.AL = al;
        State.SI = si;
        State.DI = di;
        Call(expectedReturnCs, expectedReturnIp, 0x29);
    }

    /// <summary>
    /// INT 21h AH=2Ah: returns the date in CX and DX and the day of week in AL.
    /// </summary>
    public void GetDate(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x2A);
    }

    /// <summary>
    /// INT 21h AH=2Bh: sets the date from CX and DX; status in AL.
    /// </summary>
    public void SetDate(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CheckVector();
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x2B);
    }

    /// <summary>
    /// INT 21h AH=2Ch: returns the time in CX and DX.
    /// </summary>
    public void GetTime(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x2C);
    }

    /// <summary>
    /// INT 21h AH=2Dh: sets the time from CX and DX; status in AL.
    /// </summary>
    public void SetTime(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CheckVector();
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x2D);
    }

    /// <summary>
    /// INT 21h AH=2Fh: returns the disk transfer address in ES:BX.
    /// </summary>
    public void GetDiskTransferAddress(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x2F);
    }

    /// <summary>
    /// INT 21h AH=30h: returns the DOS version in AX, BX and CX.
    /// </summary>
    public void GetDosVersion(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x30);
    }

    /// <summary>
    /// INT 21h AH=33h: gets or sets the Ctrl-Break flag (AL selects, DL sets).
    /// </summary>
    public void GetSetControlBreak(ushort expectedReturnCs, ushort expectedReturnIp, byte al, byte dl) {
        CheckVector();
        State.AL = al;
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x33);
    }

    /// <summary>
    /// INT 21h AH=34h: returns the address of the InDOS flag in ES:BX.
    /// </summary>
    public void GetInDosFlagAddress(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x34);
    }

    /// <summary>
    /// INT 21h AH=35h: returns the address of interrupt vector AL in ES:BX.
    /// </summary>
    public void GetInterruptVector(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x35);
    }

    /// <summary>
    /// INT 21h AH=36h: returns the free space of drive DL in AX, BX, CX and DX.
    /// </summary>
    public void GetFreeDiskSpace(ushort expectedReturnCs, ushort expectedReturnIp, byte dl) {
        CheckVector();
        State.DL = dl;
        Call(expectedReturnCs, expectedReturnIp, 0x36);
    }

    /// <summary>
    /// INT 21h AH=38h: gets or sets country information (AL, BX) via the buffer at DS:DX.
    /// </summary>
    public void SetCountryCode(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort bx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.AL = al;
        State.BX = bx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x38);
    }

    /// <summary>
    /// INT 21h AH=39h: creates the directory at DS:DX; error code in AX with CF set on failure.
    /// </summary>
    public void CreateDirectory(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x39);
    }

    /// <summary>
    /// INT 21h AH=39h: creates the directory at DS:DX, when its name is the constant
    /// <paramref name="text"/>; error code in AX with CF set on failure.
    /// </summary>
    public void CreateDirectory(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x39);
    }

    /// <summary>
    /// INT 21h AH=3Ah: removes the directory at DS:DX; error code in AX with CF set on failure.
    /// </summary>
    public void RemoveDirectory(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x3A);
    }

    /// <summary>
    /// INT 21h AH=3Ah: removes the directory at DS:DX, when its name is the constant
    /// <paramref name="text"/>; error code in AX with CF set on failure.
    /// </summary>
    public void RemoveDirectory(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x3A);
    }

    /// <summary>
    /// INT 21h AH=3Bh: changes the current directory to DS:DX; error code in AX with CF set on failure.
    /// </summary>
    public void ChangeCurrentDirectory(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x3B);
    }

    /// <summary>
    /// INT 21h AH=3Bh: changes the current directory to DS:DX, when its name is the constant
    /// <paramref name="text"/>; error code in AX with CF set on failure.
    /// </summary>
    public void ChangeCurrentDirectory(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x3B);
    }

    /// <summary>
    /// INT 21h AH=3Ch: creates or truncates the file at DS:DX with attributes CX; handle in AX.
    /// </summary>
    public void CreateFileUsingHandle(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x3C);
    }

    /// <summary>
    /// INT 21h AH=3Ch: creates or truncates the file at DS:DX with attributes CX, when its name is
    /// the constant <paramref name="text"/>; handle in AX.
    /// </summary>
    public void CreateFileUsingHandle(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x3C);
    }

    /// <summary>
    /// INT 21h AH=3Dh: opens the file or device at DS:DX with access mode AL; handle in AX.
    /// </summary>
    public void OpenFileOrDevice(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x3D);
    }

    /// <summary>
    /// INT 21h AH=3Dh: opens the file or device at DS:DX with access mode AL, when its name is the
    /// constant <paramref name="text"/>; handle in AX.
    /// </summary>
    public void OpenFileOrDevice(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x3D);
    }

    /// <summary>
    /// INT 21h AH=3Eh: closes handle BX; error code in AX with CF set on failure.
    /// </summary>
    public void CloseFileOrDevice(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x3E);
    }

    /// <summary>
    /// INT 21h AH=3Fh: reads CX bytes of handle BX into DS:DX; bytes read in AX.
    /// </summary>
    public void ReadFileOrDevice(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort bx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x3F);
    }

    /// <summary>
    /// INT 21h AH=40h: writes CX bytes from DS:DX to handle BX; bytes written in AX.
    /// </summary>
    public void WriteToFileOrDevice(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort bx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x40);
    }

    /// <summary>
    /// INT 21h AH=41h: deletes the file at DS:DX; error code in AX with CF set on failure.
    /// </summary>
    public void RemoveFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x41);
    }

    /// <summary>
    /// INT 21h AH=41h: deletes the file at DS:DX, when its name is the constant
    /// <paramref name="text"/>; error code in AX with CF set on failure.
    /// </summary>
    public void RemoveFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x41);
    }

    /// <summary>
    /// INT 21h AH=42h: moves the pointer of handle BX by CX:DX (mode AL); new position in DX:AX.
    /// </summary>
    public void MoveFilePointerUsingHandle(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x42);
    }

    /// <summary>
    /// INT 21h AH=43h: gets or sets the attributes of the file at DS:DX (AL selects) in CX.
    /// </summary>
    public void GetSetFileAttributes(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.AL = al;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x43);
    }

    /// <summary>
    /// INT 21h AH=43h: gets or sets the attributes of the file at DS:DX (AL selects) in CX, when
    /// its name is the constant <paramref name="text"/>.
    /// </summary>
    public void GetSetFileAttributes(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.AL = al;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x43);
    }

    /// <summary>
    /// INT 21h AH=44h: performs the I/O-control subfunction AL on handle BX or drive BL; result in AX and DX.
    /// </summary>
    public void IoControl(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x44);
    }

    /// <summary>
    /// INT 21h AH=45h: duplicates handle BX; new handle in AX.
    /// </summary>
    public void DuplicateFileHandle(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x45);
    }

    /// <summary>
    /// INT 21h AH=46h: forces handle CX to refer to the file of handle BX.
    /// </summary>
    public void ForceDuplicateFileHandle(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort bx) {
        CheckVector();
        State.BX = bx;
        State.CX = cx;
        Call(expectedReturnCs, expectedReturnIp, 0x46);
    }

    /// <summary>
    /// INT 21h AH=47h: writes the current directory of drive DL to the buffer at DS:SI.
    /// </summary>
    public void GetCurrentDirectory(ushort expectedReturnCs, ushort expectedReturnIp, byte dl, ushort si, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.DL = dl;
        State.SI = si;
        Call(expectedReturnCs, expectedReturnIp, 0x47);
    }

    /// <summary>
    /// INT 21h AH=48h: allocates a memory block of BX paragraphs; segment in AX.
    /// </summary>
    public void AllocateMemoryBlock(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x48);
    }

    /// <summary>
    /// INT 21h AH=49h: frees the memory block at segment ES.
    /// </summary>
    public void FreeMemoryBlock(ushort expectedReturnCs, ushort expectedReturnIp, ushort es) {
        CheckVector();
        State.ES = es;
        Call(expectedReturnCs, expectedReturnIp, 0x49);
    }

    /// <summary>
    /// INT 21h AH=4Ah: resizes the memory block at segment ES to BX paragraphs.
    /// </summary>
    public void ModifyMemoryBlock(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx, ushort es) {
        CheckVector();
        State.ES = es;
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x4A);
    }

    /// <summary>
    /// INT 21h AH=4Dh: returns the exit code of the last child process in AX.
    /// </summary>
    public void GetReturnCode(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x4D);
    }

    /// <summary>
    /// INT 21h AH=4Eh: finds the first file matching the pattern at DS:DX with attributes CX.
    /// </summary>
    public void FindFirstMatchingFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x4E);
    }

    /// <summary>
    /// INT 21h AH=4Eh: finds the first file matching the pattern at DS:DX with attributes CX, when
    /// the pattern is the constant <paramref name="text"/>.
    /// </summary>
    public void FindFirstMatchingFile(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx, ushort ds, string text) {
        CheckVector();
        CheckConstantString(ds, dx, text, 0);
        State.DS = ds;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x4E);
    }

    /// <summary>
    /// INT 21h AH=4Fh: finds the next file matching a previous find-first.
    /// </summary>
    public void FindNextMatchingFile(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x4F);
    }

    /// <summary>
    /// INT 21h AH=50h: sets the current PSP segment to BX.
    /// </summary>
    public void SetCurrentPsp(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x50);
    }

    /// <summary>
    /// INT 21h AH=51h: returns the current PSP segment in BX.
    /// </summary>
    public void GetCurrentPsp(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x51);
    }

    /// <summary>
    /// INT 21h AH=52h: returns the DOS list of lists in ES:BX.
    /// </summary>
    public void GetListOfLists(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x52);
    }

    /// <summary>
    /// INT 21h AH=55h: creates a child PSP at segment DX with the memory size in SI.
    /// </summary>
    public void CreateChildPsp(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort si) {
        CheckVector();
        State.DX = dx;
        State.SI = si;
        Call(expectedReturnCs, expectedReturnIp, 0x55);
    }

    /// <summary>
    /// INT 21h AH=57h: gets or sets the date and time of the file at DS:DX (AL selects) in CX and DX.
    /// </summary>
    public void GetSetFileDateAndTime(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.AL = al;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x57);
    }

    /// <summary>
    /// INT 21h AH=58h: gets or sets the allocation strategy or UMB link state (AL selects, BX sets).
    /// </summary>
    public void AllocationStrategyOrUpperMemoryLinkState(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x58);
    }

    /// <summary>
    /// INT 21h AH=62h: returns the current PSP segment in BX.
    /// </summary>
    public void GetPspAddress(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x62);
    }

    /// <summary>
    /// INT 21h AH=63h: returns the DBCS lead-byte table (AL selects).
    /// </summary>
    public void GetLeadByteTable(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x63);
    }

    /// <summary>
    /// INT 21h AH=66h: gets or sets the global code page table (AL selects).
    /// </summary>
    public void GetSetGlobalLoadedCodePageTable(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x66);
    }

    /// <summary>
    /// INT 21h AH=67h: sets the maximum file handle count to BX.
    /// </summary>
    public void SetHandleCount(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x67);
    }
}
