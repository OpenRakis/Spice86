namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.VGA;

/// <summary>
/// Typed helpers for the INT 10h services.
/// </summary>
public sealed class VideoServices : InterruptServiceGroup<VgaBios> {
    /// <summary>
    /// Initializes a new instance of the <see cref="VideoServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public VideoServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 10h AH=00h: sets the video mode to AL.
    /// </summary>
    public byte SetVideoMode(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SetVideoMode), () => Handler.SetVideoMode(al));
    }

    /// <summary>
    /// INT 10h AH=01h: sets the text-mode cursor shape from CX.
    /// </summary>
    public void SetCursorType(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SetCursorType), () => Handler.SetCursorType(cx));
    }

    /// <summary>
    /// INT 10h AH=02h: sets the cursor position on page BH to the row and column in DH and DL.
    /// </summary>
    public void SetCursorPosition(ushort expectedReturnCs, ushort expectedReturnIp, ushort dx, ushort bx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SetCursorPosition), () => Handler.SetCursorPosition(dx, bx));
    }

    /// <summary>
    /// INT 10h AH=03h: returns the cursor position of page BH in DX and its shape in CX.
    /// </summary>
    public (ushort cx, ushort dx) GetCursorPosition(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetCursorPosition), () => Handler.GetCursorPosition(bx));
    }

    /// <summary>
    /// INT 10h AH=04h: reads the light pen position; always reports no light pen.
    /// </summary>
    public (ushort ax, ushort cx, ushort dx, ushort bx) ReadLightPenPosition(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(ReadLightPenPosition), () => Handler.GetLightPenPosition());
    }

    /// <summary>
    /// INT 10h AH=05h: selects the active display page AL.
    /// </summary>
    public void SelectActiveDisplayPage(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SelectActiveDisplayPage), () => Handler.SelectActiveDisplayPage(al));
    }

    /// <summary>
    /// INT 10h AH=06h: scrolls up the window at CX..DX by AL lines, filling with attribute BH.
    /// </summary>
    public void ScrollPageUp(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(ScrollPageUp), () => Handler.ScrollPageUp(al, cx, dx, bx));
    }

    /// <summary>
    /// INT 10h AH=07h: scrolls down the window at CX..DX by AL lines, filling with attribute BH.
    /// </summary>
    public void ScrollPageDown(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(ScrollPageDown), () => Handler.ScrollPageDown(al, cx, dx, bx));
    }

    /// <summary>
    /// INT 10h AH=08h: returns the character and attribute at the cursor of page BH in AX.
    /// </summary>
    public ushort ReadCharacterAndAttributeAtCursor(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(ReadCharacterAndAttributeAtCursor), () => Handler.ReadCharacterAndAttributeAtCursor(bx));
    }

    /// <summary>
    /// INT 10h AH=09h: writes AL with attribute BL, CX times, at the cursor of page BH.
    /// </summary>
    public void WriteCharacterAndAttributeAtCursor(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort bx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(WriteCharacterAndAttributeAtCursor), () => Handler.WriteCharacterAndAttributeAtCursor(al, cx, bx));
    }

    /// <summary>
    /// INT 10h AH=0Ah: writes AL, CX times, at the cursor of page BH.
    /// </summary>
    public void WriteCharacterAtCursor(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort bx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(WriteCharacterAtCursor), () => Handler.WriteCharacterAtCursor(al, cx, bx));
    }

    /// <summary>
    /// INT 10h AH=0Bh: sets the border/background color or palette from BX.
    /// </summary>
    public void SetColorPaletteOrBackGroundColor(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SetColorPaletteOrBackGroundColor), () => Handler.SetColorPaletteOrBackGroundColor(bx));
    }

    /// <summary>
    /// INT 10h AH=0Ch: writes a pixel of color AL at CX,DX in graphics mode.
    /// </summary>
    public void WriteDot(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(WriteDot), () => Handler.WriteDot(al, cx, dx));
    }

    /// <summary>
    /// INT 10h AH=0Dh: returns the pixel color at CX,DX in AL.
    /// </summary>
    public byte ReadDot(ushort expectedReturnCs, ushort expectedReturnIp, ushort cx, ushort dx) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(ReadDot), () => Handler.ReadDot(cx, dx));
    }

    /// <summary>
    /// INT 10h AH=0Eh: writes AL in teletype mode on page BH.
    /// </summary>
    public void WriteTextInTeletypeMode(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort bx) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(WriteTextInTeletypeMode), () => Handler.WriteTextInTeletypeMode(al, bx));
    }

    /// <summary>
    /// INT 10h AH=0Fh: returns the video mode in AL, the active page in BH and the column count in AH.
    /// </summary>
    public (ushort ax, byte bh) GetVideoState(ushort expectedReturnCs, ushort expectedReturnIp) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetVideoState), () => Handler.ReadVideoState());
    }

    /// <summary>
    /// INT 10h AH=10h: sets palette registers from the table at ES:DX (AL selects the subfunction).
    /// </summary>
    public (ushort cx, byte dh, ushort bx) SetPaletteRegisters(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx, ushort es) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SetPaletteRegisters), () => Handler.SetPaletteRegisters(al, cx, dx, bx, es));
    }

    /// <summary>
    /// INT 10h AH=11h: font handling; AL selects the subfunction (load, get info).
    /// </summary>
    public (ushort cx, byte dl, ushort bp, ushort es) LoadFontInfo(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx, ushort bp, ushort es) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(LoadFontInfo), () => Handler.LoadFontInfo(al, cx, dx, bx, bp, es));
    }

    /// <summary>
    /// INT 10h AH=12h: video subsystem configuration; BL selects the subfunction.
    /// </summary>
    public (byte al, ushort cx, ushort bx) VideoSubsystemConfiguration(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort bx) {
        (byte resultAl, ushort? resultCx, ushort resultBx) = CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(VideoSubsystemConfiguration), () => Handler.VideoSubsystemConfiguration(al, bx));
        return (resultAl, resultCx ?? State.CX, resultBx);
    }

    /// <summary>
    /// INT 10h AH=13h: writes the string at ES:BP on page BH at row/column DH/DL (AL selects the mode).
    /// </summary>
    public void WriteString(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort bx, ushort bp, ushort es) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(WriteString), () => Handler.WriteString(al, cx, dx, bx, bp, es));
    }

    /// <summary>
    /// INT 10h AH=1Ah: gets or sets the display combination code (AL selects, BL sets).
    /// </summary>
    public (ushort ax, ushort bx) GetSetDisplayCombinationCode(ushort expectedReturnCs, ushort expectedReturnIp, byte al, byte bl) {
        (byte resultAl, ushort? resultBx) = CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetSetDisplayCombinationCode), () => Handler.GetSetDisplayCombinationCode(al, bl));
        ushort ax = (ushort)((EntryOf(nameof(GetSetDisplayCombinationCode)).SelectorValue << 8) | resultAl);
        return (ax, resultBx ?? (ushort)((State.BH << 8) | bl));
    }

    /// <summary>
    /// INT 10h AH=1Bh: writes video functionality information to the buffer at ES:DI.
    /// </summary>
    public byte GetFunctionalityInfo(ushort expectedReturnCs, ushort expectedReturnIp, ushort bx, ushort di, ushort es) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetFunctionalityInfo), () => Handler.GetFunctionalityInfo(bx, es, di).al);
    }

    /// <summary>
    /// INT 10h AH=4Fh: VESA VBE functions (AL selects); reports functions as unsupported.
    /// </summary>
    public ushort VesaFunctions(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort bx, ushort di, ushort es) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(VesaFunctions), () => Handler.VesaFunctions(al, cx, bx, di, es));
    }
}
