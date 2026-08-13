namespace Spice86.Logging;

using Serilog;

/// <summary>
/// Creates the shared Serilog configuration used by Spice86.
/// </summary>
public static class Spice86LoggerConfigurationFactory {
    /// <summary>
    /// The shared output template used by Spice86 log sinks.
    /// </summary>
    public const string LogFormat =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u4}] [{ContextIndex}/{IP:j}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Creates a logger configuration bound to the supplied runtime state.
    /// </summary>
    /// <param name="state">The mutable runtime logging state.</param>
    /// <returns>A Serilog configuration with Spice86 filters and enrichers applied.</returns>
    public static LoggerConfiguration Create(Spice86LoggerState state) {
        return new LoggerConfiguration()
            .MinimumLevel.ControlledBy(state.LogLevelSwitch)
            .Filter.ByExcluding(_ => state.AreLogsSilenced)
            .Enrich.FromLogContext()
            .Enrich.With(new Spice86LoggerStateEnricher(state));
    }
}