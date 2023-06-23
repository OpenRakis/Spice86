namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Shared.Interfaces;

/// <summary>
/// Contains programmable chips, such as the PIC and the PIT
/// </summary>
public class ProgrammableSubsystem {
    /// <summary>
    /// The Programmable Interrupt Timer
    /// </summary>
    public Timer Timer { get; }
    
    /// <summary>
    /// The dual programmable interrupt controllers.
    /// </summary>
    public DualPic DualPic { get; }

    /// <summary>
    /// Initializes a new instace.
    /// </summary>
    /// <param name="machine"></param>
    /// <param name="configuration"></param>
    /// <param name="loggerService"></param>
    /// <param name="counterConfigurator"></param>
    public ProgrammableSubsystem(Machine machine, Configuration configuration, ILoggerService loggerService, CounterConfigurator counterConfigurator) {
        DualPic = new DualPic(machine, configuration, loggerService);
        machine.RegisterIoPortHandler(DualPic);

        Timer = new Timer(machine, configuration, loggerService, DualPic, counterConfigurator);
        machine.RegisterIoPortHandler(Timer);
    }
}