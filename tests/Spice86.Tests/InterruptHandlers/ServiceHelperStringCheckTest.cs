namespace Spice86.Tests.InterruptHandlers;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;

using Xunit;

/// <summary>
/// The literal overloads of the typed service helpers (<c>Dos.PrintString(..., "HELLO")</c>)
/// check the bytes at <c>DS:DX</c> before any state change and run the same service as the
/// plain helper when they match. A different byte, or a different terminator, throws
/// <see cref="UnrecoverableException"/> with registers, flags and memory (console output
/// included: teletype writes land in the video memory the snapshot covers) untouched. The
/// check reads without memory breakpoints, so a read breakpoint on the string bytes fires
/// exactly as many times as for the plain helper.
/// </summary>
public class ServiceHelperStringCheckTest {
    private const ushort ScratchSegment = 0x7000;
    private const ushort ScratchOffset = 0x0000;
    private const ushort ExpectedReturnIp = ScratchOffset + 2;
    private const int StepLimit = 100000;
    private const ushort StringSegment = 0x4000;
    private const ushort StringOffset = 0x0020;
    private const uint StringLinear = StringSegment * 16 + StringOffset;

    private readonly ILogger _logger = Substitute.For<ILogger>();

    [Fact]
    public void PrintStringLiteralGivesTheSameResultAsThePlainCall() {
        // Arrange / Act
        (State state, byte[] memory, _) = RunPrintString(useLiteral: false);
        (State literalState, byte[] literalMemory, _) = RunPrintString(useLiteral: true);

        // Assert
        AssertSameState(literalState, state);
        literalMemory.Should().Equal(memory,
            "the literal overload must leave the same memory (console output included)");
    }

    [Fact]
    public void OpenFileLiteralGivesTheSameResultAsThePlainCall() {
        // Arrange / Act
        (State state, byte[] memory, _) = RunOpenFile(useLiteral: false);
        (State literalState, byte[] literalMemory, _) = RunOpenFile(useLiteral: true);

        // Assert
        AssertSameState(literalState, state);
        literalMemory.Should().Equal(memory);
    }

    [Fact]
    public void ADifferentByteFailsBeforeWritingAnything() {
        // Arrange
        using ServiceContractMachine machine = new(enableEms: false, enableXms: false);
        WriteString(machine.Spice86, "JELLO$"u8.ToArray());
        CSharpOverrideHelper helper = BuildHelper(machine.Spice86);
        byte[] memoryBefore = ServiceContractHarness.SnapshotMemory(machine.Spice86.Machine.Memory);
        StateSnapshot before = new(machine.Spice86.Machine.CpuState);

        // Act
        Action call = () => helper.Dos.PrintString(ScratchSegment, ExpectedReturnIp,
            StringOffset, StringSegment, "HELLO");
        call.Should().Throw<UnrecoverableException>().WithMessage("*4000:0020*");

        // Assert
        before.AssertUnchanged(machine.Spice86.Machine.CpuState);
        ServiceContractHarness.SnapshotMemory(machine.Spice86.Machine.Memory)
            .Should().Equal(memoryBefore, "a mismatched string must not change memory");
    }

    [Fact]
    public void ADifferentTerminatorFailsBeforeWritingAnything() {
        // Arrange
        using ServiceContractMachine machine = new(enableEms: false, enableXms: false);
        WriteString(machine.Spice86, "HELLO!"u8.ToArray());
        CSharpOverrideHelper helper = BuildHelper(machine.Spice86);
        byte[] memoryBefore = ServiceContractHarness.SnapshotMemory(machine.Spice86.Machine.Memory);
        StateSnapshot before = new(machine.Spice86.Machine.CpuState);

        // Act
        Action call = () => helper.Dos.PrintString(ScratchSegment, ExpectedReturnIp,
            StringOffset, StringSegment, "HELLO");
        call.Should().Throw<UnrecoverableException>().WithMessage("*4000:0025*");

        // Assert
        before.AssertUnchanged(machine.Spice86.Machine.CpuState);
        ServiceContractHarness.SnapshotMemory(machine.Spice86.Machine.Memory)
            .Should().Equal(memoryBefore, "a mismatched terminator must not change memory");
    }

    [Fact]
    public void TheStringBytesAreReadWithoutBreakpoints() {
        // Arrange / Act
        (_, _, int plainReads) = RunPrintString(useLiteral: false);
        (_, _, int literalReads) = RunPrintString(useLiteral: true);

        // Assert
        literalReads.Should().Be(plainReads,
            "the literal check must not fire the memory read breakpoint on the string bytes");
        plainReads.Should().BeGreaterThan(0, "the breakpoint setup must observe the service reads");
    }

    private (State State, byte[] Memory, int StringReads) RunPrintString(bool useLiteral) {
        using ServiceContractMachine machine = new(enableEms: false, enableXms: false);
        Spice86DependencyInjection spice86 = machine.Spice86;
        WriteString(spice86, "HELLO$"u8.ToArray());
        CSharpOverrideHelper helper = BuildHelper(spice86);
        int reads = CountStringReads(spice86, () => {
            if (useLiteral) {
                helper.Dos.PrintString(ScratchSegment, ExpectedReturnIp,
                    StringOffset, StringSegment, "HELLO");
            } else {
                helper.Dos.PrintString(ScratchSegment, ExpectedReturnIp,
                    StringOffset, StringSegment);
            }
        });
        return (spice86.Machine.CpuState,
            ServiceContractHarness.SnapshotMemory(spice86.Machine.Memory), reads);
    }

    private (State State, byte[] Memory, int StringReads) RunOpenFile(bool useLiteral) {
        using ServiceContractMachine machine = new(enableEms: false, enableXms: false);
        Spice86DependencyInjection spice86 = machine.Spice86;
        File.WriteAllText(Path.Join(machine.CDrivePath, "TEST.DAT"), "HELLO");
        WriteString(spice86, "TEST.DAT\0"u8.ToArray());
        CSharpOverrideHelper helper = BuildHelper(spice86);
        int reads = CountStringReads(spice86, () => {
            if (useLiteral) {
                helper.Dos.OpenFileOrDevice(ScratchSegment, ExpectedReturnIp,
                    0x00, StringOffset, StringSegment, "TEST.DAT");
            } else {
                helper.Dos.OpenFileOrDevice(ScratchSegment, ExpectedReturnIp,
                    0x00, StringOffset, StringSegment);
            }
        });
        return (spice86.Machine.CpuState,
            ServiceContractHarness.SnapshotMemory(spice86.Machine.Memory), reads);
    }

    private int CountStringReads(Spice86DependencyInjection spice86, Action call) {
        int reads = 0;
        BreakPoint breakpoint = new UnconditionalBreakPoint(BreakPointType.MEMORY_READ,
            (_, address) => {
                if (address >= StringLinear && address < StringLinear + 8) {
                    reads++;
                }
            }, false);
        EmulatorBreakpointsManager breakpoints = spice86.Machine.EmulatorBreakpointsManager;
        breakpoints.ToggleBreakPoint(breakpoint, true);
        try {
            call();
        } finally {
            breakpoints.ToggleBreakPoint(breakpoint, false);
        }
        return reads;
    }

    private CSharpOverrideHelper BuildHelper(Spice86DependencyInjection spice86) {
        CSharpOverrideHelper helper = new(spice86.FunctionCatalogue.FunctionInformations,
            spice86.Machine, _logger, new Configuration { HttpApiPort = 0 });
        SegmentedAddress target = spice86.Machine.InterruptVectorTable[0x21];
        State state = spice86.Machine.CpuState;
        helper.DefineFunction(target.Segment, target.Offset, _ => {
            state.CS = target.Segment;
            state.IP = target.Offset;
            int steps = 0;
            while ((state.CS != ScratchSegment || state.IP != ExpectedReturnIp)
                   && steps < StepLimit) {
                spice86.Machine.CfgCpu.ExecuteNext();
                steps++;
            }
            if (state.CS != ScratchSegment || state.IP != ExpectedReturnIp) {
                throw new UnrecoverableException(
                    $"Stub of int 0x21 did not return within {StepLimit} steps");
            }
            return () => { };
        }, false, "stub_runner");
        state.IsRunning = true;
        return helper;
    }

    private static void WriteString(Spice86DependencyInjection spice86, byte[] bytes) {
        for (int i = 0; i < bytes.Length; i++) {
            spice86.Machine.Memory.UInt8[StringLinear + i] = bytes[i];
        }
    }

    private static void AssertSameState(State actual, State expected) {
        actual.EAX.Should().Be(expected.EAX);
        actual.EBX.Should().Be(expected.EBX);
        actual.ECX.Should().Be(expected.ECX);
        actual.EDX.Should().Be(expected.EDX);
        actual.ESI.Should().Be(expected.ESI);
        actual.EDI.Should().Be(expected.EDI);
        actual.EBP.Should().Be(expected.EBP);
        actual.ESP.Should().Be(expected.ESP);
        actual.DS.Should().Be(expected.DS);
        actual.ES.Should().Be(expected.ES);
        actual.FS.Should().Be(expected.FS);
        actual.GS.Should().Be(expected.GS);
        actual.SS.Should().Be(expected.SS);
        actual.CS.Should().Be(expected.CS);
        actual.IP.Should().Be(expected.IP);
        actual.Flags.FlagRegister.Should().Be(expected.Flags.FlagRegister);
    }

    private sealed class StateSnapshot {
        private readonly uint _eax, _ebx, _ecx, _edx, _esi, _edi, _ebp, _esp;
        private readonly ushort _ds, _es, _fs, _gs, _ss, _cs, _ip;
        private readonly uint _flags;

        public StateSnapshot(State state) {
            _eax = state.EAX; _ebx = state.EBX; _ecx = state.ECX; _edx = state.EDX;
            _esi = state.ESI; _edi = state.EDI; _ebp = state.EBP; _esp = state.ESP;
            _ds = state.DS; _es = state.ES; _fs = state.FS; _gs = state.GS;
            _ss = state.SS; _cs = state.CS; _ip = state.IP;
            _flags = state.Flags.FlagRegister;
        }

        public void AssertUnchanged(State state) {
            state.EAX.Should().Be(_eax); state.EBX.Should().Be(_ebx);
            state.ECX.Should().Be(_ecx); state.EDX.Should().Be(_edx);
            state.ESI.Should().Be(_esi); state.EDI.Should().Be(_edi);
            state.EBP.Should().Be(_ebp); state.ESP.Should().Be(_esp);
            state.DS.Should().Be(_ds); state.ES.Should().Be(_es);
            state.FS.Should().Be(_fs); state.GS.Should().Be(_gs);
            state.SS.Should().Be(_ss); state.CS.Should().Be(_cs);
            state.IP.Should().Be(_ip);
            state.Flags.FlagRegister.Should().Be(_flags);
        }
    }
}
