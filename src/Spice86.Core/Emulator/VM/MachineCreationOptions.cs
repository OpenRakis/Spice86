namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Function;
using Spice86.Shared.Interfaces;

/// <summary>
/// Describes what the emulator machine will run, and how.
/// </summary>
/// <param name="ProgramExecutor">The DOS program or BIOS to be executed</param>
/// <param name="Gui">The GUI. Can be null in headless mode.</param>
/// <param name="LoggerService">The logger service implementation.</param>
/// <param name="CounterConfigurator">Timer emulation configuration.</param>
/// <param name="ExecutionFlowRecorder">Records execution data</param>
/// <param name="Configuration">The emulator configuration.</param>
/// <param name="RecordData">Whether we record execution data or not.</param>
public record MachineCreationOptions(
    ProgramExecutor ProgramExecutor, IGui? Gui, ILoggerService LoggerService,
    CounterConfigurator CounterConfigurator, ExecutionFlowRecorder ExecutionFlowRecorder,
    Configuration Configuration, bool RecordData);