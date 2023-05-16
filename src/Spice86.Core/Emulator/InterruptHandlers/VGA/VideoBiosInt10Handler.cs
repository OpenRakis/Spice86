namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// BIOS interrupt 10h handler
/// </summary>
public class VideoBiosInt10Handler : InterruptHandler {
    private readonly IVgaInterrupts _vgaCard;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="vgaCard">The VGA implementation.</param>
    public VideoBiosInt10Handler(Machine machine, ILoggerService loggerService, IVgaInterrupts vgaCard) : base(machine, loggerService) {
        _vgaCard = vgaCard;
        FillDispatchTable();
    }

    /// <summary>
    /// The interrupt vector this class handles.
    /// </summary>
    public override byte Index => 0x10;

    /// <summary>
    /// Runs the specified video BIOS function.
    /// </summary>
    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x00, new Callback(0x00, SetVideoMode));
        _dispatchTable.Add(0x01, new Callback(0x01, SetCursorType));
        _dispatchTable.Add(0x02, new Callback(0x02, SetCursorPosition));
        _dispatchTable.Add(0x03, new Callback(0x03, GetCursorPosition));
        _dispatchTable.Add(0x05, new Callback(0x05, SelectActiveDisplayPage));
        _dispatchTable.Add(0x06, new Callback(0x06, ScrollPageUp));
        _dispatchTable.Add(0x07, new Callback(0x07, ScrollPageDown));
        _dispatchTable.Add(0x08, new Callback(0x08, ReadCharacterAndAttributeAtCursor));
        _dispatchTable.Add(0x09, new Callback(0x09, WriteCharacterAndAttributeAtCursor));
        _dispatchTable.Add(0x0A, new Callback(0x0A, WriteCharacterAtCursor));
        _dispatchTable.Add(0x0B, new Callback(0x0B, SetColorPaletteOrBackGroundColor));
        _dispatchTable.Add(0x0C, new Callback(0x0C, _vgaCard.WriteDot));
        _dispatchTable.Add(0x0D, new Callback(0x0D, _vgaCard.ReadDot));
        _dispatchTable.Add(0x0E, new Callback(0x0E, WriteTextInTeletypeMode));
        _dispatchTable.Add(0x0F, new Callback(0x0F, GetVideoMode));
        _dispatchTable.Add(0x10, new Callback(0x10, GetSetPaletteRegisters));
        _dispatchTable.Add(0x11, new Callback(0x11, CharacterGeneratorRoutine));
        _dispatchTable.Add(0x12, new Callback(0x12, VideoSubsystemConfiguration));
        _dispatchTable.Add(0x13, new Callback(0x13, WriteString));
        _dispatchTable.Add(0x1A, new Callback(0x1A, VideoDisplayCombination));
        _dispatchTable.Add(0x1B, new Callback(0x1B, GetFunctionalityInfo));
    }

    /// <summary>
    /// Change current video mode to any VGA mode.
    /// </summary>
    public void SetVideoMode() => _vgaCard.SetVideoMode();

    /// <summary>
    /// Set text-mode cursor shape
    /// </summary>
    public void SetCursorType() => _vgaCard.SetCursorType();

    public void SetCursorPosition() => _vgaCard.SetCursorPosition();

    public void GetCursorPosition() => _vgaCard.GetCursorPosition();

    public void SelectActiveDisplayPage() => _vgaCard.SelectActiveDisplayPage();

    public void ScrollPageUp() => _vgaCard.ScrollPageUp();

    public void ScrollPageDown() => _vgaCard.ScrollPageDown();

    public void ReadCharacterAndAttributeAtCursor() => _vgaCard.ReadCharacterAndAttributeAtCursor();

    public void WriteCharacterAndAttributeAtCursor() => _vgaCard.WriteCharacterAndAttributeAtCursor();

    public void WriteCharacterAtCursor() => _vgaCard.WriteCharacterAtCursor();

    public void SetColorPaletteOrBackGroundColor() => _vgaCard.SetColorPaletteOrBackGroundColor();

    public void WriteTextInTeletypeMode() => _vgaCard.WriteTextInTeletypeMode();

    public void GetVideoMode() => _vgaCard.GetVideoState();

    public void GetSetPaletteRegisters() => _vgaCard.SetPaletteRegisters();

    public void CharacterGeneratorRoutine() => _vgaCard.LoadFontInfo();

    public void VideoSubsystemConfiguration() => _vgaCard.VideoSubsystemConfiguration();

    public void WriteString() => _vgaCard.WriteString();

    public void VideoDisplayCombination() => _vgaCard.GetSetDisplayCombinationCode();

    public void GetFunctionalityInfo() => _vgaCard.GetFunctionalityInfo();
}