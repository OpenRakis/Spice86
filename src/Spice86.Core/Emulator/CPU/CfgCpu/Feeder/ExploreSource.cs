namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

/// <summary>
/// Where a speculative exploration target came from. Data-sourced origins are only decoded when
/// every byte of the parsed instruction still holds the value its loader wrote
/// (<see cref="Spice86.Core.Emulator.LoadableFile.ChangedMemoryMap.HoldsLoadedValue"/>); the other
/// origins are taken from live instruction bytes and need no such check. A non-static item that
/// decodes as invalid is dropped, never added to the poison set.
/// </summary>
public enum ExploreSource {
    /// <summary>A statically-known successor of a decoded instruction (branch target, fall-through, callee).</summary>
    Static,
    /// <summary>The address after a call or int whose callee is known to return.</summary>
    CallContinuation,
    /// <summary>An entry of a jump or call table read when an indirect transfer executed.</summary>
    TableEntry,
    /// <summary>An address pushed on the stack before an indirect jump, used as a return target.</summary>
    PushedReturn,
    /// <summary>A function entry taken from the symbols file when its image loads.</summary>
    CatalogueEntry,
    /// <summary>A handler address taken from an interrupt vector the program installs.</summary>
    InterruptVector,
    /// <summary>A target value observed in a register when an indirect transfer executed.</summary>
    RegisterValue,
    /// <summary>A target read through a pointer cell (a global or a register-indirect read).</summary>
    PointerValue,
    /// <summary>A variant of an instruction the program wrote into memory.</summary>
    CodeVariant,
    /// <summary>A handler read from a record of an array a search loop walks, or from a constant
    /// record address.</summary>
    RecordEntry
}
