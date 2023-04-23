namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

public class VideoBiosInt10Handler : InterruptHandler {
    private readonly IVgaInterrupts _vgaInterruptHandler;

    public VideoBiosInt10Handler(Machine machine, ILoggerService loggerService, IVgaInterrupts vgaInterruptHandler) : base(machine, loggerService) {
        _vgaInterruptHandler = vgaInterruptHandler;
        FillDispatchTable();
    }

    /// <summary>
    ///     The interrupt vector this class handles.
    /// </summary>
    public override byte Index => 0x10;

    /// <summary>
    ///     Runs the specified video BIOS function.
    /// </summary>
    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x00, new Callback(0x00, _vgaInterruptHandler.SetVideoMode));
        _dispatchTable.Add(0x01, new Callback(0x01, _vgaInterruptHandler.SetCursorType));
        _dispatchTable.Add(0x02, new Callback(0x02, _vgaInterruptHandler.SetCursorPosition));
        _dispatchTable.Add(0x03, new Callback(0x03, _vgaInterruptHandler.GetCursorPosition));
        _dispatchTable.Add(0x05, new Callback(0x05, _vgaInterruptHandler.SelectActiveDisplayPage));
        _dispatchTable.Add(0x06, new Callback(0x06, _vgaInterruptHandler.ScrollPageUp));
        _dispatchTable.Add(0x07, new Callback(0x07, _vgaInterruptHandler.ScrollPageDown));
        _dispatchTable.Add(0x08, new Callback(0x08, _vgaInterruptHandler.ReadCharacterAndAttributeAtCursor));
        _dispatchTable.Add(0x09, new Callback(0x09, _vgaInterruptHandler.WriteCharacterAndAttributeAtCursor));
        _dispatchTable.Add(0x0A, new Callback(0x0A, _vgaInterruptHandler.WriteCharacterAtCursor));
        _dispatchTable.Add(0x0B, new Callback(0x0B, _vgaInterruptHandler.SetColorPaletteOrBackGroundColor));
        _dispatchTable.Add(0x0C, new Callback(0x0C, _vgaInterruptHandler.WriteDot));
        _dispatchTable.Add(0x0D, new Callback(0x0D, _vgaInterruptHandler.ReadDot));
        _dispatchTable.Add(0x0E, new Callback(0x0E, _vgaInterruptHandler.WriteTextInTeletypeMode));
        _dispatchTable.Add(0x0F, new Callback(0x0F, _vgaInterruptHandler.GetVideoState));
        _dispatchTable.Add(0x10, new Callback(0x10, _vgaInterruptHandler.SetPaletteRegisters));
        _dispatchTable.Add(0x11, new Callback(0x11, _vgaInterruptHandler.LoadFontInfo));
        _dispatchTable.Add(0x12, new Callback(0x12, _vgaInterruptHandler.VideoSubsystemConfiguration));
        _dispatchTable.Add(0x13, new Callback(0x13, _vgaInterruptHandler.WriteString));
        _dispatchTable.Add(0x1A, new Callback(0x1A, _vgaInterruptHandler.GetSetDisplayCombinationCode));
        _dispatchTable.Add(0x1B, new Callback(0x1B, () => _vgaInterruptHandler.GetFunctionalityInfo()));
    }
}