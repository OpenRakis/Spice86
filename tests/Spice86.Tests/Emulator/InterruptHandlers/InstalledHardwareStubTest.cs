namespace Spice86.Tests.Emulator.InterruptHandlers;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;

using Xunit;

/// <summary>
/// Runs each emulator-installed hardware interrupt stub like a real delivery (flags, CS and IP pushed,
/// interrupts disabled inside) and asserts it leaves every general and segment register unchanged.
/// The generated-code contract treats an installed handler as a black box preserving all registers;
/// this test proves that black-box rule on the real stubs.
/// </summary>
public sealed class InstalledHardwareStubTest {
    [Fact]
    public void InstalledHardwareHandlerStubs_PreserveEveryRegister() {
        // Arrange: a relocated handlers segment - the flat .bin loads at F000 and would overwrite the stubs.
        using Spice86Creator creator = new(new AsmFixtures.AsmRunSettings {
            BinName = "add", InstallInterruptVectors = true, AsmHandlersSegment = 0xE000
        }, JitMode.InterpretedOnly, null);

        // Act
        using Spice86DependencyInjection dependencyInjection = creator.Create();

        // Assert
        dependencyInjection.Machine.InstalledInterruptHandlers.HardwareAddresses
            .Should().NotBeEmpty();
        foreach (SegmentedAddress handlerAddress in
                 dependencyInjection.Machine.InstalledInterruptHandlers.HardwareAddresses) {
            RunStubAndCheckRegisters(dependencyInjection.Machine, handlerAddress);
        }
    }

    private static void RunStubAndCheckRegisters(Machine machine, SegmentedAddress handlerAddress) {
        State state = machine.CpuState;
        Random random = new(0x5EED + handlerAddress.Offset);
        state.AX = (ushort)random.Next(0x10000);
        state.BX = (ushort)random.Next(0x10000);
        state.CX = (ushort)random.Next(0x10000);
        state.DX = (ushort)random.Next(0x10000);
        state.BP = (ushort)random.Next(0x10000);
        state.SI = (ushort)random.Next(0x10000);
        state.DI = (ushort)random.Next(0x10000);
        state.DS = (ushort)random.Next(0x10000);
        state.ES = (ushort)random.Next(0x10000);
        state.FS = (ushort)random.Next(0x10000);
        state.GS = (ushort)random.Next(0x10000);
        (ushort ax, ushort bx, ushort cx, ushort dx, ushort bp, ushort si, ushort di) =
            (state.AX, state.BX, state.CX, state.DX, state.BP, state.SI, state.DI);
        (ushort ds, ushort es, ushort fs, ushort gs, ushort ss, ushort sp) =
            (state.DS, state.ES, state.FS, state.GS, state.SS, state.SP);

        // Deliver like a hardware interrupt does: FLAGS, CS and IP of the interrupted code pushed,
        // execution continues at the IVT target with interrupts disabled.
        SegmentedAddress resumeAddress = new(handlerAddress.Segment, 0xFFF0);
        machine.Stack.Push16(state.Flags.FlagRegister16);
        machine.Stack.Push16(resumeAddress.Segment);
        machine.Stack.Push16(resumeAddress.Offset);
        state.CS = handlerAddress.Segment;
        state.IP = handlerAddress.Offset;
        state.InterruptFlag = false;

        bool reachedIret = false;
        AddressBreakPoint iretReturn = new(BreakPointType.CPU_EXECUTION_ADDRESS,
            resumeAddress.Linear, (_, _) => reachedIret = true, false);
        machine.EmulatorBreakpointsManager.ToggleBreakPoint(iretReturn, true);
        machine.CfgCpu.SignalEntry();
        int steps = 0;
        while (!reachedIret && steps++ < 2000) {
            machine.CfgCpu.ExecuteNext();
        }
        machine.EmulatorBreakpointsManager.ToggleBreakPoint(iretReturn, false);

        reachedIret.Should().BeTrue(
            $"the stub at {handlerAddress} must reach its iret back to the pushed address");
        state.AX.Should().Be(ax, $"the stub at {handlerAddress} must preserve AX");
        state.BX.Should().Be(bx, $"the stub at {handlerAddress} must preserve BX");
        state.CX.Should().Be(cx, $"the stub at {handlerAddress} must preserve CX");
        state.DX.Should().Be(dx, $"the stub at {handlerAddress} must preserve DX");
        state.BP.Should().Be(bp, $"the stub at {handlerAddress} must preserve BP");
        state.SI.Should().Be(si, $"the stub at {handlerAddress} must preserve SI");
        state.DI.Should().Be(di, $"the stub at {handlerAddress} must preserve DI");
        state.DS.Should().Be(ds, $"the stub at {handlerAddress} must preserve DS");
        state.ES.Should().Be(es, $"the stub at {handlerAddress} must preserve ES");
        state.FS.Should().Be(fs, $"the stub at {handlerAddress} must preserve FS");
        state.GS.Should().Be(gs, $"the stub at {handlerAddress} must preserve GS");
        state.SS.Should().Be(ss, $"the stub at {handlerAddress} must preserve SS");
        state.SP.Should().Be(sp, $"the stub at {handlerAddress} must preserve SP");
    }
}
