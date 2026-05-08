namespace Spice86.Tests.CpuTests.SingleStepTests;

/// <summary>
/// Optional <c>exception</c> object attached to a single SingleStepTests case.
/// </summary>
/// <remarks>
/// When the recorded instruction faulted on real hardware, the CPU pushed a
/// FLAGS image onto the stack as part of servicing the interrupt. The 16-bit
/// FLAGS word is stored at <see cref="FlagAddress"/> (low byte) and
/// <c>FlagAddress + 1</c> (high byte) inside <c>final.ram</c>. Because some
/// EFLAGS bits are documented as undefined for the faulting opcode, those bits
/// must be masked out before comparing the two memory bytes.
/// </remarks>
/// <param name="Number">Interrupt vector raised by the faulting instruction.</param>
/// <param name="FlagAddress">Linear address where the low byte of the pushed
/// FLAGS image was stored.</param>
public record CpuTestException(int Number, uint FlagAddress);
