namespace Spice86.Core.Emulator.Devices.Timer;

using Serilog;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;

public class CounterConfigurator {
    private readonly ILogger _logger;
    private const long DefaultInstructionsPerSecond = 1000000L;
    private readonly Configuration _configuration;

    public CounterConfigurator(Configuration configuration, ILogger logger) {
        _logger = logger;
        _configuration = configuration;
    }

    public CounterActivator InstanciateCounterActivator(State state) {
        long? instructionsPerSecond = _configuration.InstructionsPerSecond;
        if (instructionsPerSecond == null && _configuration.GdbPort != null) {
            // With GDB, force to instructions per seconds as time based timers could perturbate steps
            instructionsPerSecond = DefaultInstructionsPerSecond;
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _logger.Warning("Forcing Counter to use instructions per seconds since in GDB mode. If speed is too slow or too fast adjust the --InstructionsPerSecond parameter");
            }
        }

        if (instructionsPerSecond != null) {
            return new CyclesCounterActivator(state, instructionsPerSecond.Value, _configuration.TimeMultiplier);
        }

        return new TimeCounterActivator(_configuration.TimeMultiplier);
    }
}