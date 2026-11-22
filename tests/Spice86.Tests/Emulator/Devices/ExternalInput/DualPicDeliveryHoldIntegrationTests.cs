namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Checks <see cref="DualPic.HoldDelivery" /> on the <c>externalint</c> program, whose timer handler sets DX
/// to 1, and on <c>CSharpOverrideHelper.CheckExternalEvents</c>.
/// </summary>
public sealed class DualPicDeliveryHoldIntegrationTests {
    private const byte TimerVector = 0x08;

    [Fact]
    public void EmulatedRunUnderHoldRunsNoHandlerAndListsTheRequest() {
        // Arrange
        using Spice86Creator creator = new("externalint", maxCycles: 0xFFFFFFF, enablePit: true);
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        int timerInterrupts = 0;
        machine.EmulatorBreakpointsManager.ToggleBreakPoint(
            new AddressBreakPoint(BreakPointType.CPU_INTERRUPT, TimerVector, (_, _) => timerInterrupts++, false), true);
        InterruptDeliveryHold hold = machine.DualPic.HoldDelivery();

        // Act
        dependencyInjection.ProgramExecutor.Run();

        // Assert: the handler never ran, and the hold lists the timer request the program left pending
        timerInterrupts.Should().Be(0);
        machine.CpuState.DX.Should().Be(0);
        hold.HeldDeliveries.Should().Contain(delivery =>
            delivery.Irq == 0 && delivery.VectorNumber == TimerVector && delivery.Address.Segment == 0xF000);
        hold.HeldDeliveries[0].Cycles.Should().BeGreaterThan(0);

        // Assert: the request is delivered once the hold is released
        hold.Dispose();
        machine.DualPic.ComputeVectorNumber().Should().Be(TimerVector);
    }

    [Fact]
    public void OverrideRunUnderHoldRunsNoHandler() {
        // Arrange: generate and compile the program from a discovery run with interrupts delivered
        using AsmRunScope discovery = new(AsmFixtureCatalog.ExternalInt());
        GeneratedCodeMachineTestRunner runner = new();
        GeneratedCSharpProgram generatedProgram = runner.GenerateProgramAndSource(discovery);
        using CompiledGeneratedOverride compiled =
            runner.CompileGeneratedProgram(discovery.Fixture.Settings, generatedProgram);
        InterruptDeliveryHold? hold = null;
        AsmRunSettings heldSettings = discovery.Fixture.Settings with {
            ConfigureMachine = machine => hold = machine.DualPic.HoldDelivery()
        };

        // Act
        using AsmFixtureRun run = AsmFixtureRunner.RunSettingsWithOverride(heldSettings, compiled.Supplier);

        // Assert: the handler never ran
        run.Machine.CpuState.DX.Should().Be(0);
        hold.Should().NotBeNull();
        hold!.IsReleased.Should().BeFalse();
        hold.Dispose();
        run.Machine.DualPic.IsDeliveryHeld.Should().BeFalse();
    }

    [Fact]
    public void CheckExternalEventsUnderHoldDeliversNothingAndPumpsNoDeviceEvents() {
        // Arrange: a pending timer request, interrupts enabled, and a device event that is due
        using Spice86Creator creator = new(binName: "jump2");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        CSharpOverrideHelper helper = new(new Dictionary<SegmentedAddress, FunctionInformation>(), machine,
            Substitute.For<ILogger>(), new Configuration { HttpApiPort = 0 });
        machine.CpuState.CS = 0x1000;
        machine.CpuState.IP = 0x0010;
        machine.CpuState.InterruptFlag = true;
        machine.DualPic.ActivateIrq(0);
        int deviceEvents = 0;
        machine.EmulationLoopScheduler.AddEvent(_ => deviceEvents++, 0);
        InterruptDeliveryHold hold = machine.DualPic.HoldDelivery();

        // Act
        helper.CheckExternalEvents(0x1000, 0x0010);

        // Assert: no interrupt call happened, the event is still queued and the request is listed
        machine.CpuState.CS.Should().Be(0x1000);
        machine.CpuState.IP.Should().Be(0x0010);
        machine.CpuState.InterruptFlag.Should().BeTrue();
        deviceEvents.Should().Be(0);
        hold.HeldDeliveries.Should().ContainSingle();
        hold.HeldDeliveries[0].Irq.Should().Be(0);
        hold.HeldDeliveries[0].Address.Should().Be(new SegmentedAddress(0x1000, 0x0010));

        // Act: with the hold released and interrupts disabled, the same call pumps device events again
        hold.Dispose();
        machine.CpuState.InterruptFlag = false;
        helper.CheckExternalEvents(0x1000, 0x0010);

        // Assert
        deviceEvents.Should().Be(1);
        machine.DualPic.IrqCheck.Should().BeTrue();
    }
}
