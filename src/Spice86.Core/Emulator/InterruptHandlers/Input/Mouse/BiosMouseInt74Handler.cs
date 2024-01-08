namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Implementation for callback 74
/// Handler for interrupt 0x74, which is used by the BIOS to update the mouse position.
/// Currently also installs the mouse driver ASM code when installing its own code in memory.
/// </summary>
public class BiosMouseInt74Handler : IInterruptHandler {
    private readonly DualPic _hardwareInterruptHandler;
    private readonly InMemoryAddressSwitcher _driverAddressSwitcher;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="hardwareInterruptHandler">The too programmable interrupt controllers.</param>
    /// <param name="memory">The memory bus.</param>
    public BiosMouseInt74Handler(DualPic hardwareInterruptHandler, IIndexable memory) {
        _hardwareInterruptHandler = hardwareInterruptHandler;
        _driverAddressSwitcher = new(memory);
    }

    /// <inheritdoc />
    public byte VectorNumber => 0x74;

    public void SetMouseDriverAddress(SegmentedAddress driverAddress) => _driverAddressSwitcher.SetAddress(driverAddress.Segment, driverAddress.Offset);

    /// <inheritdoc />
    public SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        // Mouse driver implementation:
        //  - Create a Far ret, this is the default mouse driver called when nothing else is there :)
        //  - Create a modifiable Far call instruction to the default mouse driver address
        //  - Create a callback (0x74) that will prepare mouse driver call
        //  - Create an IRET

        // Write ASM
        // Default mouse driver: nothing, just a far ret
        _driverAddressSwitcher.DefaultAddress = memoryAsmWriter.GetCurrentAddressCopy();
        memoryAsmWriter.WriteFarRet();

        // Entry point to the interrupt handler
        SegmentedAddress interruptHandlerAddress = memoryAsmWriter.GetCurrentAddressCopy();
        // Far call to default driver, can be changed via _inMemoryAddressSwitcher
        memoryAsmWriter.WriteFarCallToSwitcherDefaultAddress(_driverAddressSwitcher);
        // Write a callback that will EOI PIC after mouse driver execution
        memoryAsmWriter.RegisterAndWriteCallback(VectorNumber, AfterMouseDriverExecution);
        // Write IRET
        memoryAsmWriter.WriteIret();

        return interruptHandlerAddress;
    }

    /// <summary>
    /// Prepares execution before the Mouse handler is called.
    /// </summary>
    public void AfterMouseDriverExecution() => _hardwareInterruptHandler.AcknowledgeInterrupt(12);
}