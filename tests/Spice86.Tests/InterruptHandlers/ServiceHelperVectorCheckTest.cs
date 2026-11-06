namespace Spice86.Tests.InterruptHandlers;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
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

    [Fact]
    public void DirectServiceFailsBeforeWritingAnythingOnHookedVector() {
        // Arrange
        using ServiceContractMachine machine = new(enableEms: false, enableXms: false);
        Spice86DependencyInjection spice86 = machine.Spice86;
        CSharpOverrideHelper helper = new(spice86.FunctionCatalogue.FunctionInformations,
            spice86.Machine, _logger, new Configuration { HttpApiPort = 0 });

        spice86.Machine.InterruptVectorTable[0x11] = new SegmentedAddress(0x1234, 0x0010);

        byte[] memoryBefore = ServiceContractHarness.SnapshotMemory(spice86.Machine.Memory);
        StateSnapshot before = new(spice86.Machine.CpuState);

        // Act / Assert
        Action viaHelper = () => helper.Equipment.GetEquipmentList(0x5000, 0x0100);
        viaHelper.Should().Throw<UnrecoverableException>()
            .WithMessage("*0x11*");

        before.AssertUnchanged(spice86.Machine.CpuState);
        ServiceContractHarness.SnapshotMemory(spice86.Machine.Memory)
            .Should().Equal(memoryBefore, "a hooked vector must not change memory");
    }

    [Fact]
    public void DirectServiceFailsBeforeWritingAnythingOnModifiedStubBytes() {
        // Arrange: the vector still points to the installed stub, but a byte of the
        // stub itself was overwritten; the direct path must refuse to run.
        using ServiceContractMachine machine = new(enableEms: false, enableXms: false);
        Spice86DependencyInjection spice86 = machine.Spice86;
        CSharpOverrideHelper helper = new(spice86.FunctionCatalogue.FunctionInformations,
            spice86.Machine, _logger, new Configuration { HttpApiPort = 0 });

        SegmentedAddress installed = spice86.Machine.InterruptVectorTable[0x11];
        spice86.Machine.Memory.UInt8[installed.Segment, (ushort)(installed.Offset + 4)] = 0x90;

        byte[] memoryBefore = ServiceContractHarness.SnapshotMemory(spice86.Machine.Memory);
        StateSnapshot before = new(spice86.Machine.CpuState);

        // Act / Assert
        Action viaHelper = () => helper.Equipment.GetEquipmentList(0x5000, 0x0100);
        viaHelper.Should().Throw<UnrecoverableException>();

        before.AssertUnchanged(spice86.Machine.CpuState);
        ServiceContractHarness.SnapshotMemory(spice86.Machine.Memory)
            .Should().Equal(memoryBefore, "a refused direct call must not change memory");
    }

    [Fact]
    public void EveryDirectServiceHasTheCallbackAndIretStubShape() {
        // Arrange / Assert: a service may be declared Direct only when its installed stub
        // is exactly `call far [callback]; iret` (FE 38 <vector low> 00 CF) — a stub with
        // real behavior must never be bypassed.
        using ServiceContractMachine machine = new(enableEms: true, enableXms: true);
        Spice86DependencyInjection spice86 = machine.Spice86;

        int directCount = 0;
        foreach (IInterruptHandler handler in
                 spice86.Machine.InstalledInterruptHandlers.Handlers.Values) {
            foreach (InterruptServiceEntry entry in
                     ServiceContractHarness.DeclaredEntries(handler)) {
                if (entry.ExecutionPath != ServiceExecutionPath.Direct) {
                    continue;
                }
                directCount++;
                SegmentedAddress stub = spice86.Machine.InterruptVectorTable[handler.VectorNumber];
                byte[] expected = [0xFE, 0x38, handler.VectorNumber, 0x00, 0xCF];
                for (int i = 0; i < expected.Length; i++) {
                    byte actual = spice86.Machine.Memory.UInt8[
                        stub.Segment, (ushort)(stub.Offset + i)];
                    actual.Should().Be(expected[i],
                        "Direct service {0} of vector 0x{1:X2} needs the callback+iret stub byte {2}",
                        entry.Name, handler.VectorNumber, i);
                }
            }
        }
        directCount.Should().BeGreaterThan(0,
            "the contract-driven check must cover at least one Direct service");
    }

    [Fact]
    public void RawDosIoControlResolvesDuplicatedDosHandlesWithoutWritingRegisters() {
        // Arrange
        using ServiceContractMachine machine = new(enableEms: false, enableXms: false);
        Spice86DependencyInjection spice86 = machine.Spice86;
        CSharpOverrideHelper helper = new(spice86.FunctionCatalogue.FunctionInformations,
            spice86.Machine, _logger, new Configuration { HttpApiPort = 0 });
        DosFileOperationResult created = helper.Dos.CreateFile("RAW.DAT", 0);
        created.IsError.Should().BeFalse();
        created.Value.Should().NotBeNull();
        ushort originalHandle = (ushort)created.Value.GetValueOrDefault();
        helper.Dos.ForceDuplicateHandle(originalHandle, 19).IsError.Should().BeFalse();
        helper.Dos.CloseFile(originalHandle).IsError.Should().BeFalse();
        StateSnapshot before = new(spice86.Machine.CpuState);
        // Act
        (DosFileOperationResult result, byte? al, ushort dx) = helper.Dos.IoControl(0, 0, 0, 19, 0);
        (DosFileOperationResult closed, _, _) = helper.Dos.IoControl(0, 0, 0, originalHandle, 0);
        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().Be((uint)dx);
        al.Should().BeNull();
        closed.IsError.Should().BeTrue();
        closed.Value.Should().Be((uint)DosErrorCode.InvalidHandle);
        before.AssertUnchanged(spice86.Machine.CpuState);
        helper.Dos.CloseFile(19).IsError.Should().BeFalse();
    }

    [Fact]
    public void InvalidAttributeOperationKeepsCpuExceptionsAtInterruptBoundary() {
        // Arrange
        using ServiceContractMachine machine = new(enableEms: false, enableXms: false);
        Spice86DependencyInjection spice86 = machine.Spice86;
        CSharpOverrideHelper helper = new(spice86.FunctionCatalogue.FunctionInformations,
            spice86.Machine, _logger, new Configuration { HttpApiPort = 0 });
        File.WriteAllText(Path.Join(machine.CDrivePath, "ATTRIB.DAT"), "data");
        State state = spice86.Machine.CpuState;
        state.AX = 0x4302;
        state.DS = 0x0800;
        state.DX = 0x0100;
        state.CarryFlag = true;
        spice86.Machine.Memory.LoadData(0x8100, "C:\\ATTRIB.DAT\0"u8.ToArray());
        StateSnapshot before = new(state);
        // Act / Assert: a raw API reports the error without reading or writing CPU state.
        (DosFileOperationResult result, _) = helper.Dos.GetSetFileAttributes(2, "C:\\ATTRIB.DAT", 0);
        result.IsError.Should().BeTrue();
        result.Value.Should().Be((uint)DosErrorCode.FunctionNumberInvalid);
        before.AssertUnchanged(state);
        DosInt21Handler handler = (DosInt21Handler)spice86.Machine.InstalledInterruptHandlers.Handlers[0x21];
        Action interrupt = () => handler.GetSetFileAttributes(false);
        interrupt.Should().Throw<UnhandledOperationException>();
        state.CarryFlag.Should().BeFalse();
        state.AX.Should().Be(0x4302);
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
