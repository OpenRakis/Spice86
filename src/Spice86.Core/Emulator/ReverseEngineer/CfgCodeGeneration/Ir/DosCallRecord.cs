namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>What the constant facts proved about an <c>int 21h</c> site right after
/// <see cref="IrConstants"/>: the service, its declared contract and the pointer registers the
/// comment needs (<see langword="null"/> when a needed register is not a known constant there).
/// </summary>
internal sealed record DosCallRecord(byte Service, ServiceContract? Contract, ushort? Dx, ushort? Cx, ushort? Bx);
