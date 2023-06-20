namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

public class BiosMouseInt74Handler : InterruptHandler {
    private readonly ExtendedBiosDataArea _extendedBiosDataArea;

    public BiosMouseInt74Handler(Machine machine, ILoggerService loggerService, ExtendedBiosDataArea extendedBiosDataArea) : base(machine, loggerService) {
        _extendedBiosDataArea = extendedBiosDataArea;
    }

    public override byte Index => 0x74;
    public override void Run() {
        _machine.MouseDriver.Update();
        _machine.DualPic.AcknowledgeInterrupt(12);
    }
}