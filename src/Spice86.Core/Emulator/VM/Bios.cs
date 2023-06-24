namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.SystemClock;
using Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// The emulated BIOS
/// </summary>
public class Bios {
    /// <summary>
    /// INT15H handler.
    /// </summary>
    public SystemBiosInt15Handler SystemBiosInt15Handler { get; }

    /// <summary>
    /// INT1A handler.
    /// </summary>
    public SystemClockInt1AHandler SystemClockInt1AHandler { get; }

    /// <summary>
    /// BIOS INT8H timer handler.
    /// </summary>
    public TimerInt8Handler TimerInt8Handler { get; }

    /// <summary>
    /// BIOS INT11H equipment determination handler.
    /// </summary>
    public BiosEquipmentDeterminationInt11Handler BiosEquipmentDeterminationInt11Handler { get; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="timer">The programmable interval timer chip.</param>
    /// <param name="dualPic">The two programmable interrupt controllers</param>
    public Bios(Machine machine, ILoggerService loggerService, Timer timer, DualPic dualPic) {
        TimerInt8Handler = new TimerInt8Handler(machine, loggerService, timer, dualPic);
        machine.RegisterCallbackHandler(TimerInt8Handler);
        BiosEquipmentDeterminationInt11Handler = new BiosEquipmentDeterminationInt11Handler(machine, loggerService);
        machine.RegisterCallbackHandler(BiosEquipmentDeterminationInt11Handler);
        SystemBiosInt15Handler = new SystemBiosInt15Handler(machine, loggerService);
        machine.RegisterCallbackHandler(SystemBiosInt15Handler);
        SystemClockInt1AHandler = new SystemClockInt1AHandler(machine, loggerService, TimerInt8Handler);
        machine.RegisterCallbackHandler(SystemClockInt1AHandler);
    }
}