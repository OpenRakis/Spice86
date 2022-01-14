namespace Spice86.Emulator.Devices.Timer;

using Serilog;

using Spice86.Emulator.Cpu;

public class CounterConfigurator {
    private static readonly ILogger _logger = Log.Logger.ForContext<CounterConfigurator>();
    private static readonly long DEFAULT_INSTRUCTIONS_PER_SECONDS = 2000000L;
    private readonly Configuration configuration;

    public CounterConfigurator(Configuration configuration) {
        this.configuration = configuration;
    }

    public ICounterActivator InstanciateCounterActivator(State state) {
        long? instructionsPerSecond = configuration.GetInstructionsPerSecond();
        if (instructionsPerSecond == null && configuration.GetGdbPort() != null) {
            // With GDB, force to instructions per seconds as time based timers could perturbate steps
            instructionsPerSecond = DEFAULT_INSTRUCTIONS_PER_SECONDS;
            _logger.Warning("Forcing Counter to use instructions per seconds since in GDB mode. " + "If speed is too slow or too fast adjust the --instructionsPerSecond parameter");
        }

        if (instructionsPerSecond != null) {
            return new CyclesCounterActivator(state, instructionsPerSecond.Value);
        }

        return new TimeCounterActivator(configuration.GetTimeMultiplier());
    }
}