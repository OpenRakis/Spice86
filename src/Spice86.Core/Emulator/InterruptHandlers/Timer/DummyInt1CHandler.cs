namespace Spice86.Core.Emulator.InterruptHandlers.Timer;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Shared.Emulator.Memory;

/// <summary>
///     Implementation of a fake Int 1C handler.
///     Its main purpose is to provide a default handler for the timer interrupt and
///     to avoid exceptions caused by when a game does not register its own handler.
/// </summary>
public class DummyInt1CHandler : IInterruptHandler {
    /// <inheritdoc />
    public byte VectorNumber => 0x1C;

    /// <summary>The handler is a bare <c>iret</c>: it has no effect.</summary>
    [InterruptService]
    private void Tick() {
    }

    public SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        SegmentedAddress interruptHandlerAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.WriteIret();
        return interruptHandlerAddress;
    }
}