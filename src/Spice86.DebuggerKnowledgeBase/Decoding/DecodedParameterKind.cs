namespace Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Describes where a decoded parameter value comes from.
/// </summary>
public enum DecodedParameterKind {
    /// <summary>The value is held in a CPU register (e.g. AL, DS:DX).</summary>
    Register,

    /// <summary>The value is a CPU flag (e.g. CF, ZF).</summary>
    Flag,

    /// <summary>The value is read from emulated memory.</summary>
    Memory,

    /// <summary>The value is exchanged through an I/O port.</summary>
    IoPort,

    /// <summary>The value is read from the CPU stack.</summary>
    Stack,

    /// <summary>The value is encoded in the instruction itself (immediate).</summary>
    Immediate
}
