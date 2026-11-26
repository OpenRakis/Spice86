namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
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
    /// <param name="hardwareInterruptHandler">The two programmable interrupt controllers.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="registerCallTarget">Notification invoked with <c>(call site, target)</c> when
    /// the switchable driver call's pointer is written, for CFG speculative linking.</param>
    public BiosMouseInt74Handler(DualPic hardwareInterruptHandler, IIndexable memory,
        Action<SegmentedAddress, SegmentedAddress> registerCallTarget) {
        _hardwareInterruptHandler = hardwareInterruptHandler;
        _driverAddressSwitcher = new InMemoryAddressSwitcher(memory, registerCallTarget);
    }

    /// <inheritdoc />
    public byte VectorNumber => 0x74;

    /// <summary>
    /// Sets the mouse driver address.
    /// </summary>
    /// <param name="driverAddress">The Segmented Address to use</param>
    public void SetMouseDriverAddress(SegmentedAddress driverAddress) => _driverAddressSwitcher.SetAddress(driverAddress.Segment, driverAddress.Offset);

    /// <summary>
    /// Registers the driver call's current pointer target with the CFG for speculative linking:
    /// the pointer is written before the call instruction is discovered in the graph.
    /// </summary>
    public void RegisterCurrentCallTarget() => _driverAddressSwitcher.RegisterCurrentCallTarget();

    /// <inheritdoc />
    public SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        // Mouse driver implementation:
        //  - Create a Far ret, this is the default mouse driver called when nothing else is there :)
        //  - Create a memory-indirect Far call CS:[disp16] reading a switchable pointer
        //  - Create a callback (0x74) that will prepare mouse driver call
        //  - Create an IRET
        //  - Create the 4-byte far pointer the call reads, after the IRET so it is never executed

        // Write ASM
        // Default mouse driver: nothing, just a far ret
        _driverAddressSwitcher.DefaultAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.WriteFarRet();

        // Entry point to the interrupt handler
        SegmentedAddress interruptHandlerAddress = memoryAsmWriter.CurrentAddress;
        // Far call through the switchable pointer, can be changed via _inMemoryAddressSwitcher
        memoryAsmWriter.WriteCsIndirectFarCallToSwitcherDefaultAddress(_driverAddressSwitcher);
        // Write a callback that will EOI PIC after mouse driver execution
        memoryAsmWriter.RegisterAndWriteCallback(VectorNumber, AfterMouseDriverExecution);
        // Write IRET
        memoryAsmWriter.WriteIret();
        // The far pointer the call reads; sits after the IRET, outside any instruction path
        memoryAsmWriter.WriteSwitcherPointer(_driverAddressSwitcher);

        return interruptHandlerAddress;
    }

    /// <summary>
    /// Prepares execution before the Mouse handler is called.
    /// </summary>
    /// <remarks>The callback itself only acknowledges the PIC; the far call to the user driver
    /// is the stub's own instruction, not an effect of this callback.</remarks>
    [InterruptService(Io = true)]
    public void AfterMouseDriverExecution() => _hardwareInterruptHandler.AcknowledgeInterrupt(12);
}
