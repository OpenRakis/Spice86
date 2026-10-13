namespace Spice86.Core.Emulator.CPU.DescriptorTables;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// The raw 32-bit TSS (Task State Segment) field layout used by hardware task switching, and the
/// save/load operations that copy CPU state to/from it. Field offsets match the Intel-defined 386 TSS
/// (a 104-byte structure ending with the LDT selector); the I/O permission bitmap fields beyond that
/// are not used since I/O bitmap enforcement is not yet implemented.
/// </summary>
public static class TaskStateSegment {
    /// <summary>Offset of the back-link (previous task's TSS selector) field.</summary>
    public const uint LinkOffset = 0;

    /// <summary>Offset of the ring-0 ESP field.</summary>
    public const uint Esp0Offset = 4;

    /// <summary>Offset of the ring-0 SS field.</summary>
    public const uint Ss0Offset = 8;

    private const uint CrThreeOffset = 28;
    private const uint EipOffset = 32;
    private const uint EflagsOffset = 36;
    private const uint EaxOffset = 40;
    private const uint EcxOffset = 44;
    private const uint EdxOffset = 48;
    private const uint EbxOffset = 52;
    private const uint EspOffset = 56;
    private const uint EbpOffset = 60;
    private const uint EsiOffset = 64;
    private const uint EdiOffset = 68;
    private const uint EsOffset = 72;
    private const uint CsOffset = 76;
    private const uint SsOffset = 80;
    private const uint DsOffset = 84;
    private const uint FsOffset = 88;
    private const uint GsOffset = 92;
    private const uint LdtSelectorOffset = 96;

    /// <summary>The segment/EIP/LDT-selector fields read back from a TSS by a task switch, applied by
    /// the caller via full segment-load validation (which needs the already-updated CPL to order the
    /// loads correctly - CS before SS).</summary>
    public readonly record struct TssSnapshot(uint Eip, ushort Es, ushort Cs, ushort Ss, ushort Ds, ushort Fs, ushort Gs, ushort LdtSelector);

    /// <summary>
    /// Writes the current CPU state into the TSS at <paramref name="tssBase"/> (the task being left),
    /// using <paramref name="eip"/> as its resume point (the instruction after the CALL for a forward
    /// switch, or the current IP for a switch back via IRET).
    /// </summary>
    public static void SaveState(State state, IMemory memory, uint tssBase, uint eip) {
        memory.UInt32[Lin(memory, tssBase + EipOffset, isWrite: true)] = eip;
        memory.UInt32[Lin(memory, tssBase + EflagsOffset, isWrite: true)] = state.Flags.FlagRegister;
        memory.UInt32[Lin(memory, tssBase + EaxOffset, isWrite: true)] = state.EAX;
        memory.UInt32[Lin(memory, tssBase + EcxOffset, isWrite: true)] = state.ECX;
        memory.UInt32[Lin(memory, tssBase + EdxOffset, isWrite: true)] = state.EDX;
        memory.UInt32[Lin(memory, tssBase + EbxOffset, isWrite: true)] = state.EBX;
        memory.UInt32[Lin(memory, tssBase + EspOffset, isWrite: true)] = state.ESP;
        memory.UInt32[Lin(memory, tssBase + EbpOffset, isWrite: true)] = state.EBP;
        memory.UInt32[Lin(memory, tssBase + EsiOffset, isWrite: true)] = state.ESI;
        memory.UInt32[Lin(memory, tssBase + EdiOffset, isWrite: true)] = state.EDI;
        memory.UInt16[Lin(memory, tssBase + EsOffset, isWrite: true)] = state.ES;
        memory.UInt16[Lin(memory, tssBase + CsOffset, isWrite: true)] = state.CS;
        memory.UInt16[Lin(memory, tssBase + SsOffset, isWrite: true)] = state.SS;
        memory.UInt16[Lin(memory, tssBase + DsOffset, isWrite: true)] = state.DS;
        memory.UInt16[Lin(memory, tssBase + FsOffset, isWrite: true)] = state.FS;
        memory.UInt16[Lin(memory, tssBase + GsOffset, isWrite: true)] = state.GS;
        memory.UInt16[Lin(memory, tssBase + LdtSelectorOffset, isWrite: true)] = state.Ldtr.Selector;
        memory.UInt32[Lin(memory, tssBase + CrThreeOffset, isWrite: true)] = state.ControlRegisters.Cr3;
    }

    /// <summary>
    /// Reads the general-purpose registers, EFLAGS and CR3 from the TSS at <paramref name="tssBase"/>
    /// directly into <paramref name="state"/>, and returns the segment/EIP/LDT-selector fields as a
    /// <see cref="TssSnapshot"/> for the caller to apply.
    /// </summary>
    public static TssSnapshot LoadState(State state, IMemory memory, uint tssBase) {
        state.ControlRegisters.Cr3 = memory.UInt32[Lin(memory, tssBase + CrThreeOffset, isWrite: false)];
        state.Flags.FlagRegister = memory.UInt32[Lin(memory, tssBase + EflagsOffset, isWrite: false)];
        state.EAX = memory.UInt32[Lin(memory, tssBase + EaxOffset, isWrite: false)];
        state.ECX = memory.UInt32[Lin(memory, tssBase + EcxOffset, isWrite: false)];
        state.EDX = memory.UInt32[Lin(memory, tssBase + EdxOffset, isWrite: false)];
        state.EBX = memory.UInt32[Lin(memory, tssBase + EbxOffset, isWrite: false)];
        state.ESP = memory.UInt32[Lin(memory, tssBase + EspOffset, isWrite: false)];
        state.EBP = memory.UInt32[Lin(memory, tssBase + EbpOffset, isWrite: false)];
        state.ESI = memory.UInt32[Lin(memory, tssBase + EsiOffset, isWrite: false)];
        state.EDI = memory.UInt32[Lin(memory, tssBase + EdiOffset, isWrite: false)];
        return new TssSnapshot(
            Eip: memory.UInt32[Lin(memory, tssBase + EipOffset, isWrite: false)],
            Es: memory.UInt16[Lin(memory, tssBase + EsOffset, isWrite: false)],
            Cs: memory.UInt16[Lin(memory, tssBase + CsOffset, isWrite: false)],
            Ss: memory.UInt16[Lin(memory, tssBase + SsOffset, isWrite: false)],
            Ds: memory.UInt16[Lin(memory, tssBase + DsOffset, isWrite: false)],
            Fs: memory.UInt16[Lin(memory, tssBase + FsOffset, isWrite: false)],
            Gs: memory.UInt16[Lin(memory, tssBase + GsOffset, isWrite: false)],
            LdtSelector: memory.UInt16[Lin(memory, tssBase + LdtSelectorOffset, isWrite: false)]);
    }

    /// <summary>
    /// Translates a linear TSS field address through paging when it is enabled - TSS bases (like
    /// GDT/LDT/IDT bases) are linear addresses, so field reads/writes must go through the page tables
    /// once <c>CR0.PG</c> is set, exactly like an ordinary segmented memory access would.
    /// </summary>
    private static uint Lin(IMemory memory, uint linearAddress, bool isWrite) {
        return memory.Mmu.TranslateLinearAddress(linearAddress, isWrite);
    }
}
