namespace Spice86.Logging;

using Serilog.Core;
using Serilog.Events;

internal sealed class Spice86LoggerStateEnricher(Spice86LoggerState state) : ILogEventEnricher {
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) {
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("IP", state.CsIp));
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("ContextIndex", state.ContextIndex));
    }
}