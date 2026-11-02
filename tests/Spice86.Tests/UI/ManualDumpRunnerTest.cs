namespace Spice86.Tests.UI;

using FluentAssertions;

using Spice86.ViewModels.Services;

using System.IO;

using Xunit;

public class ManualDumpRunnerTest {
    [Fact]
    public void InFlightIsCompletedBeforeAnyDump() {
        // Arrange
        ManualDumpRunner runner = new();

        // Assert
        runner.InFlight.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task InFlightStaysUnfinishedUntilTheDumpEnds() {
        // Arrange
        ManualDumpRunner runner = new();
        using ManualResetEventSlim gate = new();

        // Act
        Task dump = runner.RunAsync(gate.Wait);

        // Assert
        dump.IsCompleted.Should().BeFalse();
        runner.InFlight.IsCompleted.Should().BeFalse();

        // Act: let the dump end
        gate.Set();
        await runner.InFlight;

        // Assert
        runner.InFlight.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task InFlightWaitsForEveryRunningDump() {
        // Arrange
        ManualDumpRunner runner = new();
        using ManualResetEventSlim firstGate = new();
        using ManualResetEventSlim secondGate = new();
        _ = runner.RunAsync(firstGate.Wait);
        Task second = runner.RunAsync(secondGate.Wait);

        // Act: let the second dump end while the first still runs
        secondGate.Set();
        await second;

        // Assert
        runner.InFlight.IsCompleted.Should().BeFalse();

        // Act: let the first dump end
        firstGate.Set();
        await runner.WaitAsync();

        // Assert
        runner.InFlight.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task WaitAsyncDoesNotThrowWhenADumpFailed() {
        // Arrange
        ManualDumpRunner runner = new();

        // Act
        Task dump = runner.RunAsync(() => throw new IOException("dump failed"));
        await runner.WaitAsync();

        // Assert
        Func<Task> act = () => dump;
        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task RunAsyncReturnsOnlyItsOwnDump() {
        // Arrange
        ManualDumpRunner runner = new();
        using ManualResetEventSlim gate = new();
        _ = runner.RunAsync(gate.Wait);

        // Act
        Task second = runner.RunAsync(() => { });
        await second;

        // Assert
        second.IsCompleted.Should().BeTrue();
        runner.InFlight.IsCompleted.Should().BeFalse();

        gate.Set();
        await runner.WaitAsync();
    }

    [Fact]
    public async Task InFlightIsFaultedWhenTheDumpThrows() {
        // Arrange
        ManualDumpRunner runner = new();

        // Act
        _ = runner.RunAsync(() => throw new IOException("dump failed"));

        // Assert
        Func<Task> act = () => runner.InFlight;
        await act.Should().ThrowAsync<IOException>();
    }
}
