namespace Spice86.Logging;

using Serilog.Core;
using Serilog.Events;

using Spice86.Shared.Interfaces;

/// <summary>
/// Enriches Serilog log events with properties from the logger property bag.
/// </summary>
/// <param name="propertyBag">The property bag containing emulator state information to add to log events.</param>
internal sealed class LoggerPropertyBagEnricher(ILoggerPropertyBag propertyBag) : ILogEventEnricher {
    /// <summary>
    /// Enriches the specified log event with IP and ContextIndex properties from the property bag.
    /// </summary>
    /// <param name="logEvent">The log event to enrich.</param>
    /// <param name="propertyFactory">Factory for creating log event properties.</param>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) {
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("IP", propertyBag.CsIp));
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("ContextIndex", propertyBag.ContextIndex));
    }
}