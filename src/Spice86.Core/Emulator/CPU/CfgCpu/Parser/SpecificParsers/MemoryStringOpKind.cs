namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

/// <summary>
/// Kinds of memory-based string operations. I/O string operations (INS/OUTS)
/// are handled separately as they use port I/O instead of memory copies.
/// </summary>
public enum MemoryStringOpKind {
    /// <summary>MOVS: copy [DS:SI] to [ES:DI]</summary>
    Movs,

    /// <summary>CMPS: compare [DS:SI] with [ES:DI]</summary>
    Cmps,

    /// <summary>LODS: load [DS:SI] to AL/AX/EAX</summary>
    Lods,

    /// <summary>STOS: store AL/AX/EAX to [ES:DI]</summary>
    Stos,

    /// <summary>SCAS: compare AL/AX/EAX with [ES:DI]</summary>
    Scas,
}
