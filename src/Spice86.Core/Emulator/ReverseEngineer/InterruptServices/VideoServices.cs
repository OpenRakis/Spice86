namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 10h services.
/// </summary>
public sealed class VideoServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="VideoServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public VideoServices(CSharpOverrideHelper helper) : base(helper, 0x10, ServiceSelector.Ah) {
    }

    /// <summary>
    /// INT 10h AH=00h: sets the video mode to AL.
    /// </summary>
    public void SetVideoMode(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x00);
    }

    /// <summary>
    /// INT 10h AH=01h: sets the text-mode cursor shape from CX.
    /// </summary>
    public void SetCursorType(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx) {
        CheckVector();
        State.CX = cx;
        Call(expectedReturnCs, expectedReturnIp, 0x01);
    }

    /// <summary>
    /// INT 10h AH=02h: sets the cursor position on page BH to the row and column in DH and DL.
    /// </summary>
    public void SetCursorPosition(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort bx) {
        CheckVector();
        State.BX = bx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x02);
    }

    /// <summary>
    /// INT 10h AH=03h: returns the cursor position of page BH in DX and its shape in CX.
    /// </summary>
    public void GetCursorPosition(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x03);
    }

    /// <summary>
    /// INT 10h AH=04h: reads the light pen position; always reports no light pen.
    /// </summary>
    public void ReadLightPenPosition(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x04);
    }

    /// <summary>
    /// INT 10h AH=05h: selects the active display page AL.
    /// </summary>
    public void SelectActiveDisplayPage(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x05);
    }

    /// <summary>
    /// INT 10h AH=06h: scrolls up the window at CX..DX by AL lines, filling with attribute BH.
    /// </summary>
    public void ScrollPageUp(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x06);
    }

    /// <summary>
    /// INT 10h AH=07h: scrolls down the window at CX..DX by AL lines, filling with attribute BH.
    /// </summary>
    public void ScrollPageDown(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x07);
    }

    /// <summary>
    /// INT 10h AH=08h: returns the character and attribute at the cursor of page BH in AX.
    /// </summary>
    public void ReadCharacterAndAttributeAtCursor(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x08);
    }

    /// <summary>
    /// INT 10h AH=09h: writes AL with attribute BL, CX times, at the cursor of page BH.
    /// </summary>
    public void WriteCharacterAndAttributeAtCursor(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        Call(expectedReturnCs, expectedReturnIp, 0x09);
    }

    /// <summary>
    /// INT 10h AH=0Ah: writes AL, CX times, at the cursor of page BH.
    /// </summary>
    public void WriteCharacterAtCursor(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        Call(expectedReturnCs, expectedReturnIp, 0x0A);
    }

    /// <summary>
    /// INT 10h AH=0Bh: sets the border/background color or palette from BX.
    /// </summary>
    public void SetColorPaletteOrBackGroundColor(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CheckVector();
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x0B);
    }

    /// <summary>
    /// INT 10h AH=0Ch: writes a pixel of color AL at CX,DX in graphics mode.
    /// </summary>
    public void WriteDot(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx) {
        CheckVector();
        State.AL = al;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x0C);
    }

    /// <summary>
    /// INT 10h AH=0Dh: returns the pixel color at CX,DX in AL.
    /// </summary>
    public void ReadDot(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        CheckVector();
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x0D);
    }

    /// <summary>
    /// INT 10h AH=0Eh: writes AL in teletype mode on page BH.
    /// </summary>
    public void WriteTextInTeletypeMode(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x0E);
    }

    /// <summary>
    /// INT 10h AH=0Fh: returns the video mode in AL, the active page in BH and the column count in AH.
    /// </summary>
    public void GetVideoState(ushort expectedReturnCs, ushort expectedReturnIp) {
        CheckVector();
        Call(expectedReturnCs, expectedReturnIp, 0x0F);
    }

    /// <summary>
    /// INT 10h AH=10h: sets palette registers from the table at ES:DX (AL selects the subfunction).
    /// </summary>
    public void SetPaletteRegisters(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx, ushort es) {
        CheckVector();
        State.ES = es;
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x10);
    }

    /// <summary>
    /// INT 10h AH=11h: font handling; AL selects the subfunction (load, get info).
    /// </summary>
    public void LoadFontInfo(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx, ushort bp, ushort es) {
        CheckVector();
        State.ES = es;
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        State.BP = bp;
        Call(expectedReturnCs, expectedReturnIp, 0x11);
    }

    /// <summary>
    /// INT 10h AH=12h: video subsystem configuration; BL selects the subfunction.
    /// </summary>
    public void VideoSubsystemConfiguration(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x12);
    }

    /// <summary>
    /// INT 10h AH=13h: writes the string at ES:BP on page BH at row/column DH/DL (AL selects the mode).
    /// </summary>
    public void WriteString(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx, ushort bp, ushort es) {
        CheckVector();
        State.ES = es;
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
        State.BP = bp;
        Call(expectedReturnCs, expectedReturnIp, 0x13);
    }

    /// <summary>
    /// INT 10h AH=1Ah: gets or sets the display combination code (AL selects, BL sets).
    /// </summary>
    public void GetSetDisplayCombinationCode(ushort expectedReturnCs, ushort expectedReturnIp, byte al, byte bl) {
        CheckVector();
        State.AL = al;
        State.BL = bl;
        Call(expectedReturnCs, expectedReturnIp, 0x1A);
    }

    /// <summary>
    /// INT 10h AH=1Bh: writes video functionality information to the buffer at ES:DI.
    /// </summary>
    public void GetFunctionalityInfo(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx, ushort di, ushort es) {
        CheckVector();
        State.ES = es;
        State.BX = bx;
        State.DI = di;
        Call(expectedReturnCs, expectedReturnIp, 0x1B);
    }

    /// <summary>
    /// INT 10h AH=4Fh: VESA VBE functions (AL selects); reports functions as unsupported.
    /// </summary>
    public void VesaFunctions(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort bx, ushort di, ushort es) {
        CheckVector();
        State.ES = es;
        State.AL = al;
        State.BX = bx;
        State.CX = cx;
        State.DI = di;
        Call(expectedReturnCs, expectedReturnIp, 0x4F);
    }
}
