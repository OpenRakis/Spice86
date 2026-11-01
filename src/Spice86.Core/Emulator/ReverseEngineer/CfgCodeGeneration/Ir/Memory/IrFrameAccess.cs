namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// One stack access whose offset from the SP the method was entered with is known:
/// <see cref="EntryOffset"/> is relative to that SP, so the return address sits at
/// <c>[0, ReturnAddressBytes)</c>, the arguments start at <c>ReturnAddressBytes</c>, the saved BP of
/// a standard frame is at <c>-2</c> and the locals are below.
/// </summary>
/// <param name="Statement">The statement performing the access.</param>
/// <param name="Register">The register the access goes through (the SP copy term).</param>
/// <param name="EntryOffset">The access offset relative to the method-entry SP.</param>
/// <param name="Width">The access width in bytes.</param>
/// <param name="IsWrite">Whether the statement writes the slot; false for a read.</param>
/// <param name="Pointer">The pointer node occurrence the access was proved from; frame-access
/// printing keys on it.</param>
internal sealed record IrFrameAccess(IrStatement Statement, RegisterIndex Register, int EntryOffset,
    int Width, bool IsWrite, SegmentedPointerNode Pointer);
