namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;

using Xunit;

public class PagingUnitTests {
    private const uint PageDirectoryBase = 0x1000;
    private const uint PageTableBase = 0x2000;
    private const uint PageBase = 0x3000;
    private const uint WriteBit = 0x2;
    private const uint AccessedBit = 0x20;
    private const uint DirtyBit = 0x40;

    private static (State state, Ram ram, PagingUnit unit) CreateIdentityMappedSetup(bool userAccessible) {
        State state = new(CpuModel.INTEL_80386);
        Ram ram = new(0x10000);
        state.ControlRegisters.Cr3 = PageDirectoryBase;

        uint userSupervisorBit = userAccessible ? 0b100u : 0u;
        WriteUInt32(ram, PageDirectoryBase, PageTableBase | 0b1u | userSupervisorBit); // present
        WriteUInt32(ram, PageTableBase, PageBase | 0b1u | userSupervisorBit); // present

        PagingUnit unit = new(state, ram);
        return (state, ram, unit);
    }

    private static void WriteUInt32(Ram ram, uint address, uint value) {
        ram.Write(address, (byte)value);
        ram.Write(address + 1, (byte)(value >> 8));
        ram.Write(address + 2, (byte)(value >> 16));
        ram.Write(address + 3, (byte)(value >> 24));
    }

    [Fact]
    public void Translate_PresentEntries_ReturnsPhysicalAddressWithOffset() {
        (State state, _, PagingUnit unit) = CreateIdentityMappedSetup(userAccessible: true);
        state.ControlRegisters.Cr0 = 1; // PE=1
        state.CS = 0; // CPL 0

        uint physicalAddress = unit.Translate(0x0234, isWrite: false);

        physicalAddress.Should().Be(PageBase + 0x234);
    }

    [Fact]
    public void Translate_PageDirectoryEntryNotPresent_ThrowsPageFaultWithNotPresentErrorCode() {
        State state = new(CpuModel.INTEL_80386);
        Ram ram = new(0x10000);
        state.ControlRegisters.Cr3 = PageDirectoryBase;
        // Page directory entry left at 0 (not present).
        PagingUnit unit = new(state, ram);

        Action act = () => unit.Translate(0x0234, isWrite: false);

        act.Should().Throw<CpuPageFaultException>()
            .Which.ErrorCode.Should().Be(0);
        state.ControlRegisters.Cr2.Should().Be(0x0234u);
    }

    [Fact]
    public void Translate_PageTableEntryNotPresent_ThrowsPageFaultWithNotPresentErrorCode() {
        State state = new(CpuModel.INTEL_80386);
        Ram ram = new(0x10000);
        state.ControlRegisters.Cr3 = PageDirectoryBase;
        WriteUInt32(ram, PageDirectoryBase, PageTableBase | 0b1u | 0b100u); // present, user-accessible
        // Page table entry left at 0 (not present).
        PagingUnit unit = new(state, ram);

        Action act = () => unit.Translate(0x0234, isWrite: false);

        act.Should().Throw<CpuPageFaultException>()
            .Which.ErrorCode.Should().Be(0);
    }

    [Fact]
    public void Translate_PageTableEntryNotPresentOnWrite_ThrowsPageFaultWithWriteBitSet() {
        State state = new(CpuModel.INTEL_80386);
        Ram ram = new(0x10000);
        state.ControlRegisters.Cr3 = PageDirectoryBase;
        WriteUInt32(ram, PageDirectoryBase, PageTableBase | 0b1u | 0b100u); // present, user-accessible
        // Page table entry left at 0 (not present).
        PagingUnit unit = new(state, ram);

        Action act = () => unit.Translate(0x0234, isWrite: true);

        act.Should().Throw<CpuPageFaultException>()
            .Which.ErrorCode.Should().Be(0b010); // P=0 (not present), W/R=1 (write)
    }

    [Fact]
    public void Translate_SupervisorOnlyPageAccessedFromUserMode_ThrowsPageFaultWithProtectionAndUserBits() {
        (State state, _, PagingUnit unit) = CreateIdentityMappedSetup(userAccessible: false);
        state.ControlRegisters.Cr0 = 1; // PE=1
        state.CS = 3; // CPL 3

        Action act = () => unit.Translate(0x0234, isWrite: false);

        act.Should().Throw<CpuPageFaultException>()
            .Which.ErrorCode.Should().Be(0b101); // P=1 (protection violation), U/S=1 (user mode)
    }

    [Fact]
    public void Translate_SupervisorOnlyPageAccessedFromSupervisorMode_Succeeds() {
        (State state, _, PagingUnit unit) = CreateIdentityMappedSetup(userAccessible: false);
        state.ControlRegisters.Cr0 = 1; // PE=1
        state.CS = 0; // CPL 0

        uint physicalAddress = unit.Translate(0x0234, isWrite: false);

        physicalAddress.Should().Be(PageBase + 0x234);
    }

    [Fact]
    public void Translate_UserAccessiblePageAccessedFromUserMode_Succeeds() {
        (State state, _, PagingUnit unit) = CreateIdentityMappedSetup(userAccessible: true);
        state.ControlRegisters.Cr0 = 1; // PE=1
        state.CS = 3; // CPL 3

        uint physicalAddress = unit.Translate(0x0234, isWrite: false);

        physicalAddress.Should().Be(PageBase + 0x234);
    }

    [Fact]
    public void Translate_SuccessfulAccess_SetsAccessedBitOnBothEntriesButNotDirty() {
        (State state, Ram ram, PagingUnit unit) = CreateIdentityMappedSetup(userAccessible: true);
        state.ControlRegisters.Cr0 = 1; // PE=1
        state.CS = 0; // CPL 0

        unit.Translate(0x0234, isWrite: false);

        (ReadUInt32(ram, PageDirectoryBase) & AccessedBit).Should().NotBe(0u);
        (ReadUInt32(ram, PageTableBase) & AccessedBit).Should().NotBe(0u);
        (ReadUInt32(ram, PageTableBase) & DirtyBit).Should().Be(0u);
    }

    [Fact]
    public void Translate_WriteAccess_SetsDirtyBitOnPageTableEntryOnly() {
        (State state, Ram ram, PagingUnit unit) = CreateIdentityMappedSetup(userAccessible: true);
        state.ControlRegisters.Cr0 = 1; // PE=1
        state.CS = 0; // CPL 0
        WriteUInt32(ram, PageDirectoryBase, ReadUInt32(ram, PageDirectoryBase) | WriteBit);
        WriteUInt32(ram, PageTableBase, ReadUInt32(ram, PageTableBase) | WriteBit);

        unit.Translate(0x0234, isWrite: true);

        (ReadUInt32(ram, PageTableBase) & DirtyBit).Should().NotBe(0u);
        (ReadUInt32(ram, PageDirectoryBase) & DirtyBit).Should().Be(0u);
    }

    [Fact]
    public void Translate_FaultingAccess_LeavesAccessedAndDirtyBitsUntouchedOnBothEntries() {
        (State state, Ram ram, PagingUnit unit) = CreateIdentityMappedSetup(userAccessible: false);
        state.ControlRegisters.Cr0 = 1; // PE=1
        state.CS = 3; // CPL 3 - not user-accessible, so this access must fault

        Action act = () => unit.Translate(0x0234, isWrite: false);

        act.Should().Throw<CpuPageFaultException>();
        (ReadUInt32(ram, PageDirectoryBase) & AccessedBit).Should().Be(0u);
        (ReadUInt32(ram, PageTableBase) & AccessedBit).Should().Be(0u);
    }

    [Fact]
    public void Translate_UserModeWriteToReadOnlyPage_ThrowsPageFaultWithWriteAndUserBits() {
        (State state, _, PagingUnit unit) = CreateIdentityMappedSetup(userAccessible: true);
        state.ControlRegisters.Cr0 = 1; // PE=1
        state.CS = 3; // CPL 3 - both entries are user-accessible but neither is writable

        Action act = () => unit.Translate(0x0234, isWrite: true);

        act.Should().Throw<CpuPageFaultException>()
            .Which.ErrorCode.Should().Be(0b111); // P=1 (protection violation), W/R=1 (write), U/S=1 (user mode)
    }

    [Fact]
    public void Translate_SupervisorModeWriteToReadOnlyPage_SucceedsRegardlessOfWriteBit() {
        (State state, _, PagingUnit unit) = CreateIdentityMappedSetup(userAccessible: true);
        state.ControlRegisters.Cr0 = 1; // PE=1
        state.CS = 0; // CPL 0 - supervisor writes ignore the Read/Write bit (CR0.WP is not implemented)

        uint physicalAddress = unit.Translate(0x0234, isWrite: true);

        physicalAddress.Should().Be(PageBase + 0x234);
    }

    [Fact]
    public void Translate_UserModeWriteRequiresBothEntriesWritable() {
        (State state, Ram ram, PagingUnit unit) = CreateIdentityMappedSetup(userAccessible: true);
        state.ControlRegisters.Cr0 = 1; // PE=1
        state.CS = 3; // CPL 3
        WriteUInt32(ram, PageDirectoryBase, ReadUInt32(ram, PageDirectoryBase) | WriteBit); // PDE writable, PTE still read-only

        Action act = () => unit.Translate(0x0234, isWrite: true);

        act.Should().Throw<CpuPageFaultException>()
            .Which.ErrorCode.Should().Be(0b111);
    }

    private static uint ReadUInt32(Ram ram, uint address) {
        return (uint)ram.Read(address)
            | ((uint)ram.Read(address + 1) << 8)
            | ((uint)ram.Read(address + 2) << 16)
            | ((uint)ram.Read(address + 3) << 24);
    }
}
