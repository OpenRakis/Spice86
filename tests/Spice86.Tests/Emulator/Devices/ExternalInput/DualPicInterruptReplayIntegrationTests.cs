namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Replays a timer interrupt under a delivery hold on the <c>tickwait</c> program, whose function at F000:002F
/// spins at F000:0032 until the IRQ 0 handler increments the tick counter at 0:0000, then stores 1 at 0:0001.
/// </summary>
public sealed class DualPicInterruptReplayIntegrationTests {
    private const byte TimerIrq = 0;
    private const byte TimerVector = 0x08;
    private const int PassToDeliverAt = 3;
    private static readonly SegmentedAddress FunctionEntry = new(0xF000, 0x002F);
    private static readonly SegmentedAddress LoopHead = new(0xF000, 0x0032);

    [Fact]
    public void RecordingRunReportsTheTickThatEndedTheWaitAtTheLoopHead() {
        // Arrange
        DeliveryRecorder deliveries = new();
        AsmRunSettings settings = AsmFixtureCatalog.TickWait().Settings with {
            ConfigureMachine = machine => machine.DualPic.DeliveryObserver = deliveries
        };

        // Act
        using AsmFixtureRun run = AsmFixtureRunner.RunSettings(settings, JitMode.InterpretedOnly);

        // Assert: the IRQ 0 request the loop waited for was taken at the loop head
        run.Machine.Memory.UInt8[0, 0x0001].Should().Be(1);
        deliveries.Deliveries.Should().Contain((TimerIrq, TimerVector, LoopHead));
    }

    [Fact]
    public void InterpreterTakesTheReplayedTickAtTheChosenPass() {
        // Arrange: no timer ticks; the observer delivers IRQ 0 at the third pass through the loop head
        ReplayObserver replay = new();
        DeliveryRecorder deliveries = new();
        InterruptDeliveryHold? hold = null;
        AsmRunSettings settings = ReplaySettings(AsmFixtureCatalog.TickWait().Settings, replay, deliveries,
            createdHold => hold = createdHold);

        // Act
        using AsmFixtureRun run = AsmFixtureRunner.RunSettings(settings, JitMode.InterpretedOnly);

        // Assert
        AssertReplayed(run, replay, deliveries, hold);
    }

    [Fact]
    public void GeneratedCodeTakesTheReplayedTickAtTheChosenPass() {
        // Arrange: generate and compile the program from a discovery run with the timer ticking
        using AsmRunScope discovery = new(AsmFixtureCatalog.TickWait());
        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(discovery);
        using CompiledGeneratedOverride compiled =
            runner.CompileGeneratedProgram(discovery.Fixture.Settings, generatedProgram);
        ReplayObserver replay = new();
        DeliveryRecorder deliveries = new();
        InterruptDeliveryHold? hold = null;
        AsmRunSettings settings = ReplaySettings(discovery.Fixture.Settings, replay, deliveries,
            createdHold => hold = createdHold);

        // Act
        using AsmFixtureRun run = AsmFixtureRunner.RunSettingsWithOverride(settings, compiled.Supplier);

        // Assert
        AssertReplayed(run, replay, deliveries, hold);
    }

    private static AsmRunSettings ReplaySettings(AsmRunSettings settings, ReplayObserver replay,
        DeliveryRecorder deliveries, Action<InterruptDeliveryHold> holdCreated) {
        return settings with {
            EnablePit = false,
            ConfigureMachine = machine => {
                machine.DualPic.DeliveryObserver = deliveries;
                holdCreated(machine.DualPic.HoldDelivery(replay));
            }
        };
    }

    private static void AssertReplayed(AsmFixtureRun run, ReplayObserver replay, DeliveryRecorder deliveries,
        InterruptDeliveryHold? hold) {
        // The handler ran once, and the function saw the tick and returned
        run.Machine.Memory.UInt8[0, 0x0000].Should().Be(1);
        run.Machine.Memory.UInt8[0, 0x0001].Should().Be(1);

        // The only interrupt taken is the replayed one, at the loop head
        deliveries.Deliveries.Should().Equal((TimerIrq, TimerVector, LoopHead));
        hold.Should().NotBeNull();
        hold?.HeldDeliveries.Should().BeEmpty();

        // Both forms ask at the function entry once and at the loop head on every pass before the tick
        replay.FunctionPointsUntilDelivery.Should().Equal(FunctionEntry, LoopHead, LoopHead, LoopHead);
    }

    private sealed class ReplayObserver : IInterruptDeliveryPointObserver {
        private int _loopHeadPasses;
        private bool _delivered;

        public List<SegmentedAddress> FunctionPointsUntilDelivery { get; } = new();

        public void OnDeliveryPoint(InterruptDeliveryHold hold, InterruptDeliveryPoint point) {
            if (_delivered || (point.Address != FunctionEntry && point.Address != LoopHead)) {
                return;
            }

            FunctionPointsUntilDelivery.Add(point.Address);
            if (point.Address == LoopHead && ++_loopHeadPasses == PassToDeliverAt) {
                hold.Deliver(TimerIrq);
                _delivered = true;
            }
        }
    }

    private sealed class DeliveryRecorder : IInterruptDeliveryObserver {
        public List<(byte Irq, byte Vector, SegmentedAddress Address)> Deliveries { get; } = new();

        public void OnInterruptDelivered(byte irq, byte vectorNumber, InterruptDeliveryPoint point) {
            Deliveries.Add((irq, vectorNumber, point.Address));
        }
    }
}
