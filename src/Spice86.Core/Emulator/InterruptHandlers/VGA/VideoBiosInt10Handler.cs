namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

public class VideoBiosInt10Handler : InterruptHandler {
    private readonly IVgaInterrupts _vgaCard;

    public VideoBiosInt10Handler(Machine machine, ILoggerService loggerService, IVgaInterrupts vgaCard) : base(machine, loggerService) {
        _vgaCard = vgaCard;
        FillDispatchTable();
    }

    /// <summary>
    ///   The interrupt vector this class handles.
    /// </summary>
    public override byte Index => 0x10;

    /// <summary>
    ///   Runs the specified video BIOS function.
    /// </summary>
    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x00, new Callback(0x00, _vgaCard.SetVideoMode));
        _dispatchTable.Add(0x01, new Callback(0x01, _vgaCard.SetCursorType));
        _dispatchTable.Add(0x02, new Callback(0x02, _vgaCard.SetCursorPosition));
        _dispatchTable.Add(0x03, new Callback(0x03, _vgaCard.GetCursorPosition));
        _dispatchTable.Add(0x05, new Callback(0x05, _vgaCard.SelectActiveDisplayPage));
        _dispatchTable.Add(0x06, new Callback(0x06, _vgaCard.ScrollPageUp));
        _dispatchTable.Add(0x07, new Callback(0x07, _vgaCard.ScrollPageDown));
        _dispatchTable.Add(0x08, new Callback(0x08, _vgaCard.ReadCharacterAndAttributeAtCursor));
        _dispatchTable.Add(0x09, new Callback(0x09, _vgaCard.WriteCharacterAndAttributeAtCursor));
        _dispatchTable.Add(0x0A, new Callback(0x0A, _vgaCard.WriteCharacterAtCursor));
        _dispatchTable.Add(0x0B, new Callback(0x0B, _vgaCard.SetColorPaletteOrBackGroundColor));
        _dispatchTable.Add(0x0E, new Callback(0x0E, _vgaCard.WriteTextInTeletypeMode));
        _dispatchTable.Add(0x0F, new Callback(0x0F, _vgaCard.GetVideoState));
        _dispatchTable.Add(0x10, new Callback(0x10, _vgaCard.SetPaletteRegisters));
        _dispatchTable.Add(0x11, new Callback(0x11, _vgaCard.LoadFontInfo));
        _dispatchTable.Add(0x12, new Callback(0x12, _vgaCard.VideoSubsystemConfiguration));
        _dispatchTable.Add(0x13, new Callback(0x13, _vgaCard.WriteString));
        _dispatchTable.Add(0x1A, new Callback(0x1A, _vgaCard.GetSetDisplayCombinationCode));
        _dispatchTable.Add(0x1B, new Callback(0x1B, () => _vgaCard.GetFunctionalityInfo()));
    }
}