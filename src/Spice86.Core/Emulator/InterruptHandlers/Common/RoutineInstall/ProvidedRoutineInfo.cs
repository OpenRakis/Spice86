namespace Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Information about an emulator-provided ASM routine that has been written into emulated memory.
/// Exposed by <see cref="EmulatorProvidedCodeRegistry"/> so that the debugger UI can mark the
/// corresponding instructions as "not part of the program".
/// </summary>
/// <param name="Start">First byte of the routine in emulated memory.</param>
/// <param name="ByteLength">Length of the routine in bytes.</param>
/// <param name="Name">Name registered in the function catalogue (e.g. "provided_interrupt_handler_21").</param>
/// <param name="Subsystem">Logical subsystem of the routine (e.g. "Interrupt 21h", "Mouse driver").</param>
public sealed record ProvidedRoutineInfo(
    SegmentedAddress Start,
    int ByteLength,
    string Name,
    string Subsystem);
