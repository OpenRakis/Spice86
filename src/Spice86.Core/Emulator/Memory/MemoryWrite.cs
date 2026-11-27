namespace Spice86.Core.Emulator.Memory;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// One write to the memory bus, with the width it was issued with, as an <see cref="IMemoryWriteObserver"/>
/// receives it.
/// </summary>
/// <param name="Address">Linear address of the first byte: the address of a linear write, or the MMU's
/// translation of the segment and offset of a segmented one. The A20 gate has not masked it yet
/// (<see cref="A20Gate.TransformAddress(uint)"/> gives the address the bus stores the byte at, which the
/// breakpoints see). The other bytes of a segmented write go to the next offsets in the same segment: a
/// 16-bit write at offset FFFFh puts its high byte at offset 0.</param>
/// <param name="Width">Width of the write: <see cref="BitWidth.BYTE_8"/>, <see cref="BitWidth.WORD_16"/> or
/// <see cref="BitWidth.DWORD_32"/>.</param>
/// <param name="Value">The bytes written, read as a little-endian number: the byte at
/// <paramref name="Address"/> is the low byte.</param>
public readonly record struct MemoryWrite(uint Address, BitWidth Width, uint Value);
