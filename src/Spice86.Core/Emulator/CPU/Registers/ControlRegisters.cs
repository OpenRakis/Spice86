namespace Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// The 386 control registers (CR0, CR2, CR3, CR4). Only the bits meaningful without FPU emulation are
/// exposed as named accessors; the raw register values are still stored in full so that
/// <c>MOV CRn, r32</c> round-trips every bit, including ones this emulator does not act upon.
/// </summary>
public class ControlRegisters {
    /// <summary>CR0: machine status and mode control.</summary>
    public uint Cr0 { get; set; }

    /// <summary>CR2: the linear address that caused the most recent page fault.</summary>
    public uint Cr2 { get; set; }

    /// <summary>CR3: the physical base address of the page directory.</summary>
    public uint Cr3 { get; set; }

    /// <summary>CR4: extended feature control. The 386 defines no CR4 bits; reserved for later CPU models.</summary>
    public uint Cr4 { get; set; }

    /// <summary>Protection Enable bit (CR0 bit 0): true once the CPU has entered protected mode.</summary>
    public bool ProtectionEnable {
        get => (Cr0 & 0x1) != 0;
        set => Cr0 = SetBit(Cr0, 0, value);
    }

    /// <summary>Monitor Coprocessor bit (CR0 bit 1). Stored for round-tripping only; no FPU is emulated.</summary>
    public bool MonitorCoprocessor {
        get => (Cr0 & 0x2) != 0;
        set => Cr0 = SetBit(Cr0, 1, value);
    }

    /// <summary>Task Switched bit (CR0 bit 3): set by the CPU on every task switch, cleared by CLTS.</summary>
    public bool TaskSwitched {
        get => (Cr0 & 0x8) != 0;
        set => Cr0 = SetBit(Cr0, 3, value);
    }

    /// <summary>Extension Type bit (CR0 bit 4): reserved on the 386, present for round-tripping only.</summary>
    public bool ExtensionType {
        get => (Cr0 & 0x10) != 0;
        set => Cr0 = SetBit(Cr0, 4, value);
    }

    /// <summary>Paging Enable bit (CR0 bit 31): true when linear-to-physical paging translation is active.</summary>
    public bool PagingEnable {
        get => (Cr0 & 0x8000_0000) != 0;
        set => Cr0 = SetBit(Cr0, 31, value);
    }

    private static uint SetBit(uint register, int bitIndex, bool value) {
        uint mask = 1u << bitIndex;
        return value ? register | mask : register & ~mask;
    }
}
