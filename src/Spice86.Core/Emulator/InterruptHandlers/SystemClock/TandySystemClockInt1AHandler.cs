namespace Spice86.Core.Emulator.InterruptHandlers.SystemClock;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Devices.Sound.Tandy;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.Memory;

/// <summary>Extends clock services with native BIOS sound operations.</summary>
public sealed class TandySystemClockInt1AHandler : SystemClockInt1AHandler {
    private readonly TandySoundDevice? _sound;

    /// <summary>Creates sound-aware clock services, including the no-DAC Tandy case.</summary>
    public TandySystemClockInt1AHandler(IMemory memory, BiosDataArea bios, RealTimeClock clock,
        IFunctionHandlerProvider functions, Stack stack, State state, ILogger logger, TandySoundDevice? sound)
        : base(memory, bios, clock, functions, stack, state, logger) {
        _sound = sound;
    }

    /// <inheritdoc />
    public override void Run(int index) {
        if (index is < 0x81 or > 0x85) {
            base.Run(index);
            return;
        }
        if (_sound?.TryHandleBiosFunction((byte)index) == true) {
            SetCarryFlag(State.CarryFlag, true);
        }
    }
}