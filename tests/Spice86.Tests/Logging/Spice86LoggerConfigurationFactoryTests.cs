namespace Spice86.Tests.Logging;

using FluentAssertions;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using Spice86.Logging;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class Spice86LoggerConfigurationFactoryTests {
    [Fact]
    public void Create_EnrichesEventsWithRuntimeState() {
        // Arrange
        Spice86LoggerState state = new() {
            ContextIndex = 7,
            CsIp = new SegmentedAddress(0x1234, 0x5678)
        };
        CollectingSink sink = new();
        ILogger logger = Spice86LoggerConfigurationFactory.Create(state)
            .WriteTo.Sink(sink)
            .CreateLogger();

        // Act
        logger.Information("Hello");

        // Assert
        sink.Events.Should().ContainSingle();
        LogEvent logEvent = sink.Events[0];
        logEvent.Properties["ContextIndex"].Should().BeOfType<ScalarValue>();
        ScalarValue contextIndex = (ScalarValue)logEvent.Properties["ContextIndex"];
        contextIndex.Value.Should().Be(7);
        logEvent.Properties["IP"].Should().BeOfType<ScalarValue>();
        ScalarValue ip = (ScalarValue)logEvent.Properties["IP"];
        ip.Value.Should().Be("1234:5678");
    }

    [Fact]
    public void Create_HonorsRuntimeSilencingAndMinimumLevel() {
        // Arrange
        Spice86LoggerState state = new();
        state.LogLevelSwitch.MinimumLevel = LogEventLevel.Warning;
        CollectingSink sink = new();
        ILogger logger = Spice86LoggerConfigurationFactory.Create(state)
            .WriteTo.Sink(sink)
            .CreateLogger();

        // Act
        logger.Information("Ignored");
        state.AreLogsSilenced = true;
        logger.Error("Also ignored");
        state.AreLogsSilenced = false;
        logger.Warning("Written");

        // Assert
        sink.Events.Should().ContainSingle();
        sink.Events[0].Level.Should().Be(LogEventLevel.Warning);
        sink.Events[0].MessageTemplate.Text.Should().Be("Written");
    }

    [Fact]
    public void LoggerService_UsesSuppliedConfigurationFactory() {
        // Arrange
        Spice86LoggerState state = new();
        CollectingSink sink = new();
        LoggerService loggerService = new(state,
            loggerState => Spice86LoggerConfigurationFactory.Create(loggerState).WriteTo.Sink(sink));

        // Act
        loggerService.Warning("Written through wrapper");

        // Assert
        sink.Events.Should().ContainSingle();
        sink.Events[0].MessageTemplate.Text.Should().Be("Written through wrapper");
    }

    private sealed class CollectingSink : ILogEventSink {
        public List<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) {
            Events.Add(logEvent);
        }
    }
}