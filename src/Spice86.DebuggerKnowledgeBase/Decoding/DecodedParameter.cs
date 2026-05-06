namespace Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// One decoded parameter or result of a high-level call (interrupt, I/O port, ASM routine).
/// Immutable: decoders read emulator state and return parameters; the debugger UI displays them.
/// </summary>
/// <param name="Name">Human-readable parameter name (e.g. "filename", "access mode").</param>
/// <param name="Source">Where the raw value comes from (e.g. "AL", "DS:DX", "port 0x3C9").</param>
/// <param name="Kind">Storage classification of the source.</param>
/// <param name="RawValue">Raw integer value; meaningful only for Register/Flag/Immediate/IoPort. Use 0 when not applicable.</param>
/// <param name="FormattedValue">Display-ready value (e.g. "'A' (0x41)", "C:\\GAME\\FILE.DAT", "0x0F = SB Pro stereo on").</param>
/// <param name="Notes">Optional extra explanation; null if none.</param>
public sealed record DecodedParameter(
    string Name,
    string Source,
    DecodedParameterKind Kind,
    long RawValue,
    string FormattedValue,
    string? Notes);
