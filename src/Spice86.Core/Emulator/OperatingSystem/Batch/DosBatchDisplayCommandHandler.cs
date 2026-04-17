namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

internal sealed class DosBatchDisplayCommandHandler : IBatchDisplayCommandHandler {
    private readonly IVgaFunctionality _vgaFunctionality;

    internal DosBatchDisplayCommandHandler(IVgaFunctionality vgaFunctionality) {
        _vgaFunctionality = vgaFunctionality;
    }

    public void ClearScreen() {
        _vgaFunctionality.VerifyScroll(1, 0, 0, 0xFF, 0xFF, 0, 0x07);
        _vgaFunctionality.SetCursorPosition(new CursorPosition(0, 0, 0));
    }
}