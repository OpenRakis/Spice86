namespace Spice86.Logging;

using Serilog.Core;
using Serilog.Events;

using Spice86.Shared.Interfaces;

internal sealed class LoggerPropertyBagEnricher(ILoggerPropertyBag propertyBag) : ILogEventEnricher {
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) {
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("IP", propertyBag.CsIp));
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("ContextIndex", propertyBag.ContextIndex));
    }
}