using Spice86.Logging;
using Spice86.Shared.Interfaces;

namespace Spice86.Core.Emulator.Devices.Timer;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;

/// <summary>
/// A factory class that creates a new instance of one of the implementations of <see cref="CounterActivator"/> class based on the emulator configuration.
/// </summary>
public class CounterConfiguratorFactory {
    private readonly State _state;
    private readonly ILoggerService _loggerService;
    private readonly IPauseHandler _pauseHandler;
    private const long DefaultInstructionsPerSecond = 1000000L;
    private readonly Configuration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="CounterConfiguratorFactory"/> class.
    /// </summary>
    public CounterConfiguratorFactory(Configuration configuration, State state, IPauseHandler pauseHandler, ILoggerService loggerService) {
        _state = state;
        _loggerService = loggerService;
        _pauseHandler = pauseHandler;
        _configuration = configuration;
    }

    /// <summary>
    /// Creates a new instance of one of the implementations of <see cref="CounterActivator"/> class based on the emulator configuration.
    /// </summary>
    public CounterActivator InstanciateCounterActivator() {
        long? instructionsPerSecond = _configuration.InstructionsPerSecond;
        if (instructionsPerSecond == null && _configuration.GdbPort != null) {
            // With GDB, force to instructions per seconds as time based timers could perturbate steps
            instructionsPerSecond = DefaultInstructionsPerSecond;
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("Forcing Counter to use instructions per seconds since in GDB mode. If speed is too slow or too fast adjust the --InstructionsPerSecond parameter");
            }
        }

        if (instructionsPerSecond != null) {
            return new CyclesCounterActivator(_state, _pauseHandler, instructionsPerSecond.Value, _configuration.TimeMultiplier);
        }

        return new TimeCounterActivator(_pauseHandler, _configuration.TimeMultiplier);
    }
}