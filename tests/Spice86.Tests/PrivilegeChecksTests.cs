namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.DescriptorTables;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;

using Xunit;

public class PrivilegeChecksTests {
    private static State CreateProtectedModeState(byte cpl, byte iopl = 0, bool virtual8086 = false) {
        State state = new(CpuModel.INTEL_80386);
        state.ControlRegisters.Cr0 = 1; // PE=1
        state.Flags.SetFlag(Flags.Virtual8086Mode, virtual8086);
        state.CS = (ushort)((1 << 3) | cpl);
        state.IoPrivilegeLevel = iopl;
        return state;
    }

    [Fact]
    public void EnsureIoPrivilege_RealMode_NeverThrows() {
        State state = new(CpuModel.INTEL_80386);
        state.CS = 0x0003; // would be CPL 3 if interpreted in protected mode
        state.IoPrivilegeLevel = 0;

        Action act = () => PrivilegeChecks.EnsureIoPrivilege(state);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureIoPrivilege_ProtectedMode_CplBelowOrEqualIopl_DoesNotThrow() {
        State state = CreateProtectedModeState(cpl: 0, iopl: 0);

        Action act = () => PrivilegeChecks.EnsureIoPrivilege(state);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureIoPrivilege_ProtectedMode_CplAboveIopl_ThrowsGeneralProtectionFault() {
        State state = CreateProtectedModeState(cpl: 3, iopl: 0);

        Action act = () => PrivilegeChecks.EnsureIoPrivilege(state);

        act.Should().Throw<CpuGeneralProtectionFaultException>();
    }

    [Fact]
    public void EnsureIoPrivilege_ProtectedMode_CplEqualsIopl_DoesNotThrow() {
        State state = CreateProtectedModeState(cpl: 3, iopl: 3);

        Action act = () => PrivilegeChecks.EnsureIoPrivilege(state);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureIoPrivilege_Virtual8086Mode_IoplThree_DoesNotThrow() {
        State state = CreateProtectedModeState(cpl: 0, iopl: 3, virtual8086: true);

        Action act = () => PrivilegeChecks.EnsureIoPrivilege(state);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureIoPrivilege_Virtual8086Mode_IoplBelowThree_Throws() {
        State state = CreateProtectedModeState(cpl: 0, iopl: 2, virtual8086: true);

        Action act = () => PrivilegeChecks.EnsureIoPrivilege(state);

        act.Should().Throw<CpuGeneralProtectionFaultException>();
    }

    [Fact]
    public void ValidateDataSegmentLoad_RealMode_NeverThrowsEvenForBadDescriptor() {
        State state = new(CpuModel.INTEL_80386);
        SegmentDescriptorCache notPresent = new(0, 0xFFFF, accessRights: 0x00, defaultBig: false, granularity4K: false, present: false);

        Action act = () => PrivilegeChecks.ValidateDataSegmentLoad(state, SegmentRegisterIndex.DsIndex, 0x0008, notPresent);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateDataSegmentLoad_CsIndex_NeverValidated() {
        State state = CreateProtectedModeState(cpl: 0);
        SegmentDescriptorCache notPresent = new(0, 0xFFFF, accessRights: 0x00, defaultBig: false, granularity4K: false, present: false);

        Action act = () => PrivilegeChecks.ValidateDataSegmentLoad(state, SegmentRegisterIndex.CsIndex, 0x0008, notPresent);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateDataSegmentLoad_PresentWritableDataAtSufficientDpl_DoesNotThrow() {
        State state = CreateProtectedModeState(cpl: 0);
        SegmentDescriptorCache data = new(0, 0xFFFF, accessRights: 0x92, defaultBig: false, granularity4K: false, present: true);

        Action act = () => PrivilegeChecks.ValidateDataSegmentLoad(state, SegmentRegisterIndex.DsIndex, 0x0008, data);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateDataSegmentLoad_NotPresent_ThrowsSegmentNotPresent() {
        State state = CreateProtectedModeState(cpl: 0);
        SegmentDescriptorCache data = new(0, 0xFFFF, accessRights: 0x92, defaultBig: false, granularity4K: false, present: false);

        Action act = () => PrivilegeChecks.ValidateDataSegmentLoad(state, SegmentRegisterIndex.DsIndex, 0x0008, data);

        act.Should().Throw<CpuSegmentNotPresentException>();
    }

    [Fact]
    public void ValidateDataSegmentLoad_MaxOfCplAndRplExceedsDpl_ThrowsGeneralProtectionFault() {
        State state = CreateProtectedModeState(cpl: 3);
        SegmentDescriptorCache dpl0Data = new(0, 0xFFFF, accessRights: 0x92, defaultBig: false, granularity4K: false, present: true);

        Action act = () => PrivilegeChecks.ValidateDataSegmentLoad(state, SegmentRegisterIndex.DsIndex, 0x0008, dpl0Data);

        act.Should().Throw<CpuGeneralProtectionFaultException>();
    }

    [Fact]
    public void ValidateDataSegmentLoad_NonReadableCodeSegment_ThrowsGeneralProtectionFault() {
        State state = CreateProtectedModeState(cpl: 0);
        SegmentDescriptorCache nonReadableCode = new(0, 0xFFFF, accessRights: 0x98, defaultBig: false, granularity4K: false, present: true);

        Action act = () => PrivilegeChecks.ValidateDataSegmentLoad(state, SegmentRegisterIndex.DsIndex, 0x0008, nonReadableCode);

        act.Should().Throw<CpuGeneralProtectionFaultException>();
    }

    [Fact]
    public void ValidateDataSegmentLoad_ConformingCodeWithLowerDplThanCpl_DoesNotThrow() {
        State state = CreateProtectedModeState(cpl: 3);
        SegmentDescriptorCache conformingReadableCode = new(0, 0xFFFF, accessRights: 0x9E, defaultBig: false, granularity4K: false, present: true);

        Action act = () => PrivilegeChecks.ValidateDataSegmentLoad(state, SegmentRegisterIndex.DsIndex, 0x0008, conformingReadableCode);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateDataSegmentLoad_Ss_WritableDataMatchingCpl_DoesNotThrow() {
        State state = CreateProtectedModeState(cpl: 0);
        SegmentDescriptorCache data = new(0, 0xFFFF, accessRights: 0x92, defaultBig: false, granularity4K: false, present: true);

        Action act = () => PrivilegeChecks.ValidateDataSegmentLoad(state, SegmentRegisterIndex.SsIndex, 0x0008, data);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateDataSegmentLoad_Ss_NotPresent_ThrowsStackSegmentFault() {
        State state = CreateProtectedModeState(cpl: 0);
        SegmentDescriptorCache data = new(0, 0xFFFF, accessRights: 0x92, defaultBig: false, granularity4K: false, present: false);

        Action act = () => PrivilegeChecks.ValidateDataSegmentLoad(state, SegmentRegisterIndex.SsIndex, 0x0008, data);

        act.Should().Throw<CpuStackSegmentFaultException>();
    }

    [Fact]
    public void ValidateDataSegmentLoad_Ss_RplNotEqualCpl_ThrowsGeneralProtectionFault() {
        State state = CreateProtectedModeState(cpl: 0);
        SegmentDescriptorCache data = new(0, 0xFFFF, accessRights: 0x92, defaultBig: false, granularity4K: false, present: true);

        // Selector RPL (low 2 bits) is 3, which does not match CPL 0.
        Action act = () => PrivilegeChecks.ValidateDataSegmentLoad(state, SegmentRegisterIndex.SsIndex, 0x000B, data);

        act.Should().Throw<CpuGeneralProtectionFaultException>();
    }

    [Fact]
    public void ValidateDataSegmentLoad_Ss_NonWritableData_ThrowsGeneralProtectionFault() {
        State state = CreateProtectedModeState(cpl: 0);
        SegmentDescriptorCache readOnlyData = new(0, 0xFFFF, accessRights: 0x90, defaultBig: false, granularity4K: false, present: true);

        Action act = () => PrivilegeChecks.ValidateDataSegmentLoad(state, SegmentRegisterIndex.SsIndex, 0x0008, readOnlyData);

        act.Should().Throw<CpuGeneralProtectionFaultException>();
    }
}
