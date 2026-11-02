namespace Spice86.Tests.StateSerialization;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.StateSerialization;
using Spice86.Shared.Interfaces;

using Xunit;

public class DumpProgressReporterTest {
    private sealed class TestTimeProvider : ITimeProvider {
        public DateTime Now { get; set; } = new(2024, 1, 1);
    }

    private sealed class CapturingLogger : ILogger {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) {
            Messages.Add(formatter(state, exception));
        }
    }

    [Fact]
    public void PhaseEndMessageContainsElapsedMilliseconds() {
        CapturingLogger logger = new();
        DumpProgressReporter reporter = new(logger, new SystemTimeProvider());

        reporter.BeginDump("dumpdir");
        reporter.BeginPhase(DumpPhase.Memory, "spice86dumpMemoryDump.bin");
        reporter.EndPhase();

        logger.Messages.Should().Contain(m =>
            m.StartsWith("Memory dump (spice86dumpMemoryDump.bin): finished in ") && m.EndsWith(" ms"));
    }

    [Fact]
    public void StepLogsOnlyWhenTenthBoundaryIncreasesOrOnLastStep() {
        CapturingLogger logger = new();
        DumpProgressReporter reporter = new(logger, new TestTimeProvider());

        reporter.BeginDump("dumpdir");
        reporter.BeginPhase(DumpPhase.CodeMethods);
        for (int i = 1; i <= 25; i++) {
            reporter.Step(i, 25, $"method_{i}");
        }
        reporter.EndPhase();

        List<string> stepMessages = logger.Messages
            .Where(m => m.StartsWith("Code generation: method ")).ToList();
        // current * 10 / 25 increases at steps 1, 3, 5, 8, 10, 13, 15, 18, 20, 23 and 25 (the last).
        stepMessages.Should().HaveCount(11);
        stepMessages.Last().Should().Be("Code generation: method 25 / 25 (100%): method_25");
    }

    [Fact]
    public void StepLogsEveryStepForSmallTotals() {
        CapturingLogger logger = new();
        DumpProgressReporter reporter = new(logger, new TestTimeProvider());

        reporter.BeginDump("dumpdir");
        reporter.BeginPhase(DumpPhase.CodeMethods);
        for (int i = 1; i <= 3; i++) {
            reporter.Step(i, 3, $"method_{i}");
        }
        reporter.EndPhase();

        List<string> stepMessages = logger.Messages
            .Where(m => m.StartsWith("Code generation: method ")).ToList();
        stepMessages.Should().Equal(
            "Code generation: method 1 / 3 (33%): method_1",
            "Code generation: method 2 / 3 (66%): method_2",
            "Code generation: method 3 / 3 (100%): method_3");
    }

    [Fact]
    public void StepReportedIsRaisedForEveryStep() {
        DumpProgressReporter reporter = new(new CapturingLogger(), new SystemTimeProvider());
        List<(DumpPhase Phase, int Current, int Total, string Item)> steps = new();
        reporter.StepReported += (phase, current, total, item) => steps.Add((phase, current, total, item));

        reporter.BeginDump("dumpdir");
        reporter.BeginPhase(DumpPhase.CodeMethods);
        for (int i = 1; i <= 25; i++) {
            reporter.Step(i, 25, $"method_{i}");
        }
        reporter.EndPhase();

        steps.Should().HaveCount(25);
        steps.Should().OnlyContain(s => s.Phase == DumpPhase.CodeMethods && s.Total == 25);
        steps.Select(s => s.Current).Should().Equal(Enumerable.Range(1, 25));
        steps.Select(s => s.Item).Should().Equal(Enumerable.Range(1, 25).Select(i => $"method_{i}"));
    }

    [Fact]
    public void DetailRaisesMessageLoggedWithTheExactText() {
        CapturingLogger logger = new();
        DumpProgressReporter reporter = new(logger, new SystemTimeProvider());
        List<string> raised = new();
        reporter.MessageLogged += raised.Add;

        reporter.BeginDump("dumpdir");
        reporter.Detail("Code generation: method m took 1500 ms");

        raised.Last().Should().Be("Code generation: method m took 1500 ms");
        logger.Messages.Last().Should().Be("Code generation: method m took 1500 ms");
    }

    [Fact]
    public void MessageLoggedCarriesExactlyTheLoggedText() {
        CapturingLogger logger = new();
        DumpProgressReporter reporter = new(logger, new SystemTimeProvider());
        List<string> raised = new();
        reporter.MessageLogged += raised.Add;

        reporter.BeginDump("dumpdir");
        reporter.BeginPhase(DumpPhase.CpuRegisters);
        reporter.EndPhase();
        reporter.EndDump(true);

        raised.Should().Equal(logger.Messages);
        raised.Should().NotBeEmpty();
    }

    [Fact]
    public void EndDumpFailureUsesFailedWording() {
        CapturingLogger logger = new();
        DumpProgressReporter reporter = new(logger, new SystemTimeProvider());

        reporter.BeginDump("dumpdir");
        reporter.EndDump(false);

        logger.Messages.Last().Should().Contain("failed");
    }

    [Fact]
    public void StepLogsWhenASecondPassedSinceTheLastStepLine() {
        CapturingLogger logger = new();
        TestTimeProvider timeProvider = new();
        DumpProgressReporter reporter = new(logger, timeProvider);

        reporter.BeginDump("dumpdir");
        reporter.BeginPhase(DumpPhase.CodeMethods);
        reporter.Step(1, 1000, "method_1");
        for (int i = 2; i <= 5; i++) {
            timeProvider.Now = timeProvider.Now.AddMilliseconds(400);
            reporter.Step(i, 1000, $"method_{i}");
        }
        reporter.EndPhase();

        List<string> stepMessages = logger.Messages
            .Where(m => m.StartsWith("Code generation: method ")).ToList();
        stepMessages.Should().Equal(
            "Code generation: method 1 / 1000 (0%): method_1",
            "Code generation: method 4 / 1000 (0%): method_4");
    }

    [Fact]
    public void StepLineIsTheStepText() {
        CapturingLogger logger = new();
        DumpProgressReporter reporter = new(logger, new TestTimeProvider());

        reporter.BeginDump("dumpdir");
        reporter.BeginPhase(DumpPhase.CodeMethods);
        reporter.Step(2, 5, "test_F000_0000_F0000");

        logger.Messages.Last().Should().Be(
            DumpProgressReporter.StepText(DumpPhase.CodeMethods, 2, 5, "test_F000_0000_F0000"));
    }
}
