namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
///     Default BIOS IRQ handler used for hardware interrupts without a dedicated handler.
///     Ensures the IVT entries are populated from power-on and that the PIC
///     receives an End Of Interrupt acknowledgement when these vectors fire.
/// </summary>
public sealed class DefaultIrqHandler : IInterruptHandler {
    private readonly BiosDataArea _biosDataArea;
    private readonly DualPic _dualPic;
    private readonly byte _irq;
    private readonly ILoggerService _logger;

    /// <summary>
    ///     Initializes a new instance targeting the specified IRQ line.
    /// </summary>
    /// <param name="dualPic">PIC pair used to acknowledge interrupts.</param>
    /// <param name="irq">IRQ line serviced by this handler.</param>
    /// <param name="biosDataArea">BIOS data area used to record unexpected IRQs.</param>
    /// <param name="logger">Logger used for diagnostic output.</param>
    public DefaultIrqHandler(DualPic dualPic, byte irq, BiosDataArea biosDataArea, ILoggerService logger) {
        _dualPic = dualPic;
        _irq = irq;
        _biosDataArea = biosDataArea;
        _logger = logger;
    }

    /// <inheritdoc />
    public byte VectorNumber =>
        _irq >= 8
            ? (byte)(0x70 + (_irq - 8))
            : (byte)(0x08 + _irq);

    /// <inheritdoc />
    public SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        SegmentedAddress handlerAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.RegisterAndWriteCallback(HandleIrq);
        memoryAsmWriter.WriteIret();
        return handlerAddress;
    }

    /// <summary>
    ///     Handles an unexpected IRQ by recording it and acknowledging the PIC so the system continues to run.
    /// </summary>
    private void HandleIrq() {
        _logger.Warning("BIOS default IRQ handler serviced unhandled IRQ{Irq}", _irq);

        byte mask = ComputeBiosIrqMask();
        _logger.Debug("BIOS_LAST_UNEXPECTED_IRQ previous value 0x{Prev:X2}", _biosDataArea.LastUnexpectedIrq);

        _biosDataArea.LastUnexpectedIrq = mask;
        _logger.Debug("BIOS_LAST_UNEXPECTED_IRQ updated to 0x{Mask:X2}", mask);

        _dualPic.AcknowledgeInterrupt(_irq);
        _logger.Verbose("Acknowledged IRQ{Irq} on the PIC", _irq);
        _dualPic.SetIrqMask(_irq, true);
        _logger.Verbose("Masked IRQ{Irq} to prevent repeated unexpected interrupts", _irq);
    }

    /// <summary>
    ///     Computes the mask value written in the BIOS data area for an unexpected IRQ.
    /// </summary>
    /// <returns>Bit mask describing the unexpected IRQ.</returns>
    private byte ComputeBiosIrqMask() {
        if (_irq < 8) {
            return (byte)(1 << _irq);
        }

        // Cascade line bit (IRQ2) is what the BIOS records for secondary IRQs.
        return 0x04;
    }
}