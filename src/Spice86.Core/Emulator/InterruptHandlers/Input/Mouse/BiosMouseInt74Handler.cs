namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Implementation for callback 74
/// Handler for interrupt 0x74, which is used by the BIOS to update the mouse position.
/// </summary>
public class BiosMouseInt74Handler : IInterruptHandler, IAsmUserRoutineHandler {
    private readonly DualPic _hardwareInterruptHandler;
    private readonly IMouseDriver _mouseDriver;
    private readonly IMemory _memory;

    public BiosMouseInt74Handler(IMouseDriver mouseDriver, DualPic hardwareInterruptHandler, IMemory memory) {
        _mouseDriver = mouseDriver;
        _hardwareInterruptHandler = hardwareInterruptHandler;
        _memory = memory;
    }

    /// <inheritdoc />
    public byte VectorNumber => 0x74;

    private uint? _userRoutineAddressLocation;
    private SegmentedAddress? _defaultUserRoutineAddress;


    /// <inheritdoc />
    public void SetUserRoutineAddress(ushort segment, ushort offset) {
        if (_userRoutineAddressLocation is null) {
            return;
        }
        // Write the address to the far call instruction in memory (yay self modifying code!)
        _memory.UInt16[_userRoutineAddressLocation.Value] = offset;
        _memory.UInt16[_userRoutineAddressLocation.Value + 2] = segment;
    }

    /// <inheritdoc />
    public void DisableUserRoutine() {
        if (_defaultUserRoutineAddress is null) {
            return;
        }

        SetUserRoutineAddress(_defaultUserRoutineAddress.Segment, _defaultUserRoutineAddress.Offset);
    }

    /// <inheritdoc />
    public SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        // Mouse driver implementation:
        //  - Create a callback (0x74) that will call the Run method
        //  - Create a Far call instruction that is calling a dummy handler by default, but can be modified to call anything
        //  - Create a callback (0x90) that does the cleanup
        //  - Create an IRET
        //  - Create the dummy handler (just a FAR RET)
        
        // Write ASM
        SegmentedAddress interruptHandlerAddress = new SegmentedAddress(memoryAsmWriter.CurrentAddress);
        memoryAsmWriter.RegisterAndWriteCallback(VectorNumber, PrepareMouseDriverExecution);
        // Far call to nothing for now, we will overwrite the address to the default handler later
        _userRoutineAddressLocation = memoryAsmWriter.WriteFarCall(new SegmentedAddress(0, 0)).ToPhysical();
        memoryAsmWriter.RegisterAndWriteCallback(0x90, PostMouseDriverExecution);
        memoryAsmWriter.WriteIret();
        // Write the default handler (that does nothing) 
        _defaultUserRoutineAddress = new SegmentedAddress(memoryAsmWriter.CurrentAddress);
        memoryAsmWriter.WriteFarRet();
        // Write the default handler in the far call instruction
        DisableUserRoutine();

        // Finally when everything is properly setup, signal the mouse driver how it can set and unset user handler
        _mouseDriver.UserRoutineHandler = this;
        return interruptHandlerAddress;
    }

    /// <summary>
    /// Prepares execution before the User mouse handler is called.
    /// </summary>
    public void PrepareMouseDriverExecution() {
        _mouseDriver.BeforeUserHandlerExecution();
        _hardwareInterruptHandler.AcknowledgeInterrupt(12);
    }

    /// <summary>
    /// Does the cleanup after the User mouse handler is called.
    /// </summary>
    public void PostMouseDriverExecution() {
        _mouseDriver.AfterMouseDriverExecution();
    }

}