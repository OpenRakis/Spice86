using Spice86.Logging;
using Spice86.Shared.Interfaces;

namespace Spice86.Core.Emulator.Devices.Timer;

using Serilog;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;

public class CounterConfigurator {
    private readonly ILoggerService _loggerService;
    private const long DefaultInstructionsPerSecond = 1000000L;
    private readonly Configuration _configuration;

    public CounterConfigurator(Configuration configuration, ILoggerService loggerService) {
        _loggerService = loggerService;
        _configuration = configuration;
    }

    public CounterActivator InstanciateCounterActivator(ICpuState state) {
        long? instructionsPerSecond = _configuration.InstructionsPerSecond;
        if (instructionsPerSecond == null && _configuration.GdbPort != null) {
            // With GDB, force to instructions per seconds as time based timers could perturbate steps
            instructionsPerSecond = DefaultInstructionsPerSecond;
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("Forcing Counter to use instructions per seconds since in GDB mode. If speed is too slow or too fast adjust the --InstructionsPerSecond parameter");
            }
        }

        if (instructionsPerSecond != null) {
            return new CyclesCounterActivator(state, instructionsPerSecond.Value, _configuration.TimeMultiplier);
        }

        return new TimeCounterActivator(_configuration.TimeMultiplier);
    }
}