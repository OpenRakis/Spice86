namespace Spice86.DebuggerKnowledgeBase.Instructions;

/// <summary>
/// High-level human-readable description of an x86/386 instruction.
/// </summary>
/// <remarks>
/// This is a debugger reminder — not a full ISA reference. The intent is to give the
/// reverse-engineer a one-line name plus a short paragraph reminding them what the
/// instruction does, what it touches, and the typical reason a compiler/programmer
/// would emit it. Full per-encoding parameter decoding is intentionally out of scope.
/// </remarks>
/// <param name="Mnemonic">Canonical mnemonic (matches Iced's <c>Mnemonic</c> enum name).</param>
/// <param name="Name">Long English name, e.g. "Move", "Compare", "Jump if Not Zero".</param>
/// <param name="Summary">One-sentence description of the operation.</param>
/// <param name="Uses">Registers / flags / memory the instruction reads or writes.</param>
/// <param name="Purpose">Why this instruction is typically emitted.</param>
public sealed record InstructionInfo(
    string Mnemonic,
    string Name,
    string Summary,
    string Uses,
    string Purpose);
