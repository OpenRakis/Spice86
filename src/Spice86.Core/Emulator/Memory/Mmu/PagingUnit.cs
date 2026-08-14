namespace Spice86.Core.Emulator.Memory.Mmu;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>
/// CR3-rooted two-level page-directory/page-table walk (32-bit paging, 4KB pages), translating a
/// linear address to a physical address when <see cref="Registers.ControlRegisters.PagingEnable"/> is
/// set. Enforces the Present bit and the combined User/Supervisor and Read/Write protection of the
/// page-directory and page-table entry against the current CPL and access kind, matching the 80386's
/// documented combining rules: an access is user-accessible only if BOTH entries are User, and (since
/// this emulator does not implement CR0.WP) only a user-mode write is checked against the Read/Write
/// bit - supervisor writes are always permitted.
/// </summary>
public sealed class PagingUnit {
    private const uint PresentBit = 0x1;
    private const uint WriteBit = 0x2;
    private const uint UserSupervisorBit = 0x4;
    private const uint AccessedBit = 0x20;
    private const uint DirtyBit = 0x40;
    private const uint TableAddressMask = 0xFFFF_F000;
    private const uint PageOffsetMask = 0xFFF;

    private readonly State _state;
    private readonly IMemoryDevice _ram;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="state">The CPU state, used to read CR3, CR2 (written on fault), and CPL.</param>
    /// <param name="ram">The raw memory device backing page-directory/page-table reads.</param>
    public PagingUnit(State state, IMemoryDevice ram) {
        _state = state;
        _ram = ram;
    }

    /// <summary>
    /// Translates a linear address to a physical address, walking the page directory and page table
    /// rooted at CR3. Throws <see cref="CpuPageFaultException"/> (and sets CR2) on any not-present or
    /// protection violation. Sets the Accessed bit on both entries, and the Dirty bit on the PTE when
    /// <paramref name="isWrite"/> is set, matching the 80386's documented behavior: these bits are only
    /// set once the FULL two-level walk succeeds - a fault at either level leaves every entry's
    /// Accessed/Dirty bits untouched, even if an earlier level was valid.
    /// </summary>
    /// <param name="linearAddress">The linear address to translate.</param>
    /// <param name="isWrite">Whether the access is a write, used for the Dirty bit and the Read/Write protection check.</param>
    public uint Translate(uint linearAddress, bool isWrite) {
        uint pageDirectoryIndex = linearAddress >> 22;
        uint pageTableIndex = (linearAddress >> 12) & 0x3FF;
        uint pageOffset = linearAddress & PageOffsetMask;

        uint pageDirectoryEntryAddress = (_state.ControlRegisters.Cr3 & TableAddressMask) + pageDirectoryIndex * 4;
        uint pageDirectoryEntry = ReadUInt32(pageDirectoryEntryAddress);
        EnsurePresent(pageDirectoryEntry, linearAddress, isWrite);

        uint pageTableEntryAddress = (pageDirectoryEntry & TableAddressMask) + pageTableIndex * 4;
        uint pageTableEntry = ReadUInt32(pageTableEntryAddress);
        EnsurePresent(pageTableEntry, linearAddress, isWrite);

        EnsureProtection(pageDirectoryEntry, pageTableEntry, linearAddress, isWrite);

        MarkAccessed(pageDirectoryEntryAddress, pageDirectoryEntry);
        MarkAccessed(pageTableEntryAddress, pageTableEntry);
        if (isWrite && (pageTableEntry & DirtyBit) == 0) {
            WriteUInt32(pageTableEntryAddress, pageTableEntry | DirtyBit);
        }

        return (pageTableEntry & TableAddressMask) + pageOffset;
    }

    private void MarkAccessed(uint entryAddress, uint entry) {
        if ((entry & AccessedBit) == 0) {
            WriteUInt32(entryAddress, entry | AccessedBit);
        }
    }

    private void EnsurePresent(uint entry, uint linearAddress, bool isWrite) {
        if ((entry & PresentBit) == 0) {
            throw CreatePageFault(linearAddress, protectionViolation: false, isWrite);
        }
    }

    private void EnsureProtection(uint pageDirectoryEntry, uint pageTableEntry, uint linearAddress, bool isWrite) {
        if (_state.Cpl != 3) {
            return; // supervisor accesses ignore U/S and R/W entirely (CR0.WP is not implemented).
        }
        bool userAccessible = (pageDirectoryEntry & UserSupervisorBit) != 0 && (pageTableEntry & UserSupervisorBit) != 0;
        if (!userAccessible) {
            throw CreatePageFault(linearAddress, protectionViolation: true, isWrite);
        }
        bool writable = (pageDirectoryEntry & WriteBit) != 0 && (pageTableEntry & WriteBit) != 0;
        if (isWrite && !writable) {
            throw CreatePageFault(linearAddress, protectionViolation: true, isWrite);
        }
    }

    private uint ReadUInt32(uint address) {
        return (uint)_ram.Read(address)
            | ((uint)_ram.Read(address + 1) << 8)
            | ((uint)_ram.Read(address + 2) << 16)
            | ((uint)_ram.Read(address + 3) << 24);
    }

    private void WriteUInt32(uint address, uint value) {
        _ram.Write(address, (byte)value);
        _ram.Write(address + 1, (byte)(value >> 8));
        _ram.Write(address + 2, (byte)(value >> 16));
        _ram.Write(address + 3, (byte)(value >> 24));
    }

    private CpuPageFaultException CreatePageFault(uint linearAddress, bool protectionViolation, bool isWrite) {
        _state.ControlRegisters.Cr2 = linearAddress;
        bool userMode = _state.Cpl == 3;
        ushort errorCode = (ushort)((protectionViolation ? 1u : 0u) | (isWrite ? 0b10u : 0u) | (userMode ? 0b100u : 0u));
        if (Environment.GetEnvironmentVariable("SPICE86_TRACE_PAGING") is not null) {
            System.IO.Directory.CreateDirectory("tmp");
            System.IO.File.AppendAllText("tmp/paging_trace.txt",
                $"PF linear=0x{linearAddress:X8} errorCode=0b{Convert.ToString(errorCode, 2).PadLeft(3, '0')} protectionViolation={protectionViolation} isWrite={isWrite} cpl={_state.Cpl}\n");
        }
        string reason = protectionViolation ? "protection violation" : "not present";
        return new CpuPageFaultException($"Page fault at linear address 0x{linearAddress:X8} ({reason})", errorCode);
    }
}
