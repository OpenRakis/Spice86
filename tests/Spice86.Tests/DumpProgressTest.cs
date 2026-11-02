namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.StateSerialization;
using Spice86.Tests.Utility;

using Xunit;

/// <summary>
/// Checks that a real run through <see cref="Spice86DependencyInjection"/> reports the recorded-data
/// dump through <see cref="DumpProgressReporter"/>: devices are shut down before the dump starts,
/// phases run in order, and failures still raise the finished event.
/// </summary>
public class DumpProgressTest {
    private static readonly DumpPhase[] ExpectedPhaseOrder = [
        DumpPhase.CpuRegisters,
        DumpPhase.Memory,
        DumpPhase.Listing,
        DumpPhase.CfgSnapshot,
        DumpPhase.CfgBlocks,
        DumpPhase.CfgPartitions,
        DumpPhase.CfgReload,
        DumpPhase.ExecutionFlow,
        DumpPhase.CodeAnalysis,
        DumpPhase.CodePlan,
        DumpPhase.CodeMethods,
        DumpPhase.CodeSource,
        DumpPhase.CodeProject,
        DumpPhase.Symbols,
        DumpPhase.Breakpoints
    ];

    [Fact]
    public void DumpReportsStartedPhasesInOrderAndFinished() {
        using Spice86Creator creator = new("add");
        using Spice86DependencyInjection di = creator.Create();
        List<DumpPhase> phases = new();
        int started = 0;
        int finished = 0;
        di.DumpProgressReporter.DumpStarted += () => started++;
        di.DumpProgressReporter.PhaseBegan += phases.Add;
        di.DumpProgressReporter.DumpFinished += () => finished++;

        di.ProgramExecutor.Run();

        started.Should().Be(1);
        finished.Should().Be(1);
        phases.Should().Equal(ExpectedPhaseOrder);
    }

    [Fact]
    public void DumpFinishedIsRaisedWhenAPhaseThrows() {
        using TempFile dumpDirectory = new(nameof(DumpFinishedIsRaisedWhenAPhaseThrows));
        using Spice86Creator creator = new("add", recordedDataDirectory: dumpDirectory.Path);
        using Spice86DependencyInjection di = creator.Create();
        int finished = 0;
        di.DumpProgressReporter.DumpFinished += () => finished++;
        Directory.Delete(dumpDirectory.Path, recursive: true);

        Exception? error = Record.Exception(() => di.ProgramExecutor.Run());

        error.Should().NotBeNull();
        finished.Should().Be(1);
    }

    [Fact]
    public void DevicesAreShutDownBeforeTheDumpStarts() {
        using TempFile dumpDirectory = new(nameof(DevicesAreShutDownBeforeTheDumpStarts));
        using Spice86Creator creator = new("add", recordedDataDirectory: dumpDirectory.Path);
        using Spice86DependencyInjection di = creator.Create();
        bool? machineDisposedAtDumpStart = null;
        di.DumpProgressReporter.DumpStarted += () => machineDisposedAtDumpStart = di.Machine.IsDisposed;

        di.ProgramExecutor.Run();

        machineDisposedAtDumpStart.Should().BeTrue(
            "the dump must only run after the shutdown coordinator stopped all devices");
        // The dump lands in a program-hash subdirectory of the recorded-data directory.
        Directory.EnumerateFiles(dumpDirectory.Path, "spice86dumpCfgGeneratedOverrides.cs",
            SearchOption.AllDirectories).Should().NotBeEmpty(
            "the generated C# dump proves every dump step can run after device shutdown");
    }

    [Fact]
    public void TwoWritesDoNotOverlap() {
        // Arrange
        using Spice86Creator creator = new("add");
        using Spice86DependencyInjection di = creator.Create();
        di.ProgramExecutor.Run();
        EmulationStateDataWriter writer = di.EmulatorStateSerializer.EmulationStateDataWriter;
        using ManualResetEventSlim firstDumpMayFinish = new();
        using ManualResetEventSlim secondDumpStarted = new();
        int started = 0;
        di.DumpProgressReporter.DumpStarted += () => {
            if (Interlocked.Increment(ref started) == 1) {
                firstDumpMayFinish.Wait();
            } else {
                secondDumpStarted.Set();
            }
        };

        // Act
        Thread first = new(writer.Write);
        Thread second = new(writer.Write);
        first.Start();
        second.Start();
        bool secondStartedWhileFirstWrites = secondDumpStarted.Wait(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        firstDumpMayFinish.Set();
        first.Join();
        second.Join();

        // Assert
        secondStartedWhileFirstWrites.Should().BeFalse(
            "a second write must wait for the write in progress to end");
        started.Should().Be(2);
    }
}
