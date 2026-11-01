namespace Spice86.Tests.InterruptHandlers;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// A typed service helper must refuse to run when the program hooked the interrupt
/// vector: it throws <see cref="UnrecoverableException"/> before writing any register
/// or memory. With the vector intact, the call goes through.
/// </summary>
public class ServiceHelperVectorCheckTest {
    private readonly ILogger _logger = Substitute.For<ILogger>();

    [Fact]
    public void HookedVectorFailsBeforeWritingAnything() {
        // Arrange
        using ServiceContractMachine machine = new(enableEms: false, enableXms: false);
        Spice86DependencyInjection spice86 = machine.Spice86;
        CSharpOverrideHelper helper = new(spice86.FunctionCatalogue.FunctionInformations,
            spice86.Machine, _logger, new Configuration { HttpApiPort = 0 });

        SegmentedAddress installed = spice86.Machine.InterruptVectorTable[0x21];
        spice86.Machine.InterruptVectorTable[0x21] = new SegmentedAddress(0x1234, 0x0010);

        byte[] memoryBefore = ServiceContractHarness.SnapshotMemory(spice86.Machine.Memory);
        StateSnapshot before = new(spice86.Machine.CpuState);

        // Act / Assert
        Action viaHelper = () => helper.Dos.PrintString(0x5000, 0x0100, 0x4000, 0x0020);
        viaHelper.Should().Throw<UnrecoverableException>()
            .WithMessage("*0x21*");

        Action viaCall = () => helper.InterruptServiceCall(0x5000, 0x0100, 0x21);
        viaCall.Should().Throw<UnrecoverableException>()
            .WithMessage("*0x21*");

        before.AssertUnchanged(spice86.Machine.CpuState);
        ServiceContractHarness.SnapshotMemory(spice86.Machine.Memory)
            .Should().Equal(memoryBefore, "a hooked vector must not change memory");

        // With the vector restored, the check passes and the call runs the override.
        spice86.Machine.InterruptVectorTable[0x21] = installed;
        spice86.Machine.CpuState.IsRunning = true;
        helper.DefineFunction(installed.Segment, installed.Offset,
            _ => helper.InterruptRet(), false, "stub");
        viaHelper.Should().NotThrow();
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
