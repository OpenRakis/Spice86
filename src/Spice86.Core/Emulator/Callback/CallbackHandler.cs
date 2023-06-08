namespace Spice86.Core.Emulator.Callback;

using System.Collections.Generic;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// The class that handles emulation of memory based callbacks.
/// </summary>
public class CallbackHandler : IndexBasedDispatcher {
    private const ushort CallbackSize = 4;

    /// <summary>
    /// Map of all the callback addresses
    /// </summary>
    private readonly Dictionary<byte, SegmentedAddress> _callbackAddresses = new();

    /// <summary>
    /// Segment where to install the callbacks code in memory
    /// </summary>
    private readonly ushort _callbackHandlerSegment;

    /// <summary>
    /// Offset in this segment so that new callbacks are written to a fresh location 
    /// </summary>
    private ushort _offset = 0;

    private readonly Memory _memory;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="interruptHandlerSegment">Segment where to install the callback code in memory.</param>
    public CallbackHandler(Machine machine, ILoggerService loggerService, ushort interruptHandlerSegment) : base(machine, loggerService) {
        _memory = machine.Memory;
        _callbackHandlerSegment = interruptHandlerSegment;
    }

    /// <summary>
    /// Adds the callback to the dispatch table.
    /// </summary>
    /// <param name="callback">The callback to add.</param>
    /// <exception cref="ArgumentException">If an item with the same callback index has already been added</exception>
    public void AddCallback(ICallback callback) {
        AddService(callback.Index, callback);
    }

    /// <summary>
    /// Returns the map of all the callback addresses.
    /// </summary>
    /// <returns>Map of all the callback addresses</returns>
    public Dictionary<byte, SegmentedAddress> GetCallbackAddresses() {
        return _callbackAddresses;
    }

    /// <summary>
    /// Installs all the callback in the dispatch table in emulated memory.
    /// </summary>
    public void InstallAllCallbacksInInterruptTable() {
        foreach (ICallback callback in _dispatchTable.Values) {
            InstallCallbackInInterruptTable(callback);
        }
    }

    /// <summary>
    /// Returns a copy of the RAM with all the callback instructions replaced by NOPs.
    /// This is to make the RAM dumps loadable with ghidra.
    /// </summary>
    /// <returns>A copy of the RAM with all the callback instructions replaced by NOPs.</returns>
    public byte[] NopCallbackInstructionInRamCopy() {
        byte[] res = (byte[])_memory.Ram.Clone();
        foreach (KeyValuePair<byte, SegmentedAddress> entry in _callbackAddresses) {
            byte intNo = entry.Key;
            uint address = entry.Value.ToPhysical();
            res[address] = 0xCD; //INT
            res[address + 1] = intNo;
            res[address + 2] = 0x90; // NOP
        }
        return res;
    }

    /// <inheritdoc/>
    protected override UnhandledOperationException GenerateUnhandledOperationException(int index) {
        return new UnhandledCallbackException(_machine, index);
    }

    /// <summary>
    /// Installs the given callback in the interrupt table in emulated memory.
    /// </summary>
    /// <param name="callback">The callback to install.</param>
    private void InstallCallbackInInterruptTable(ICallback callback) {
        // Use either the provided segment or the default one.
        if (callback.InterruptHandlerSegment.HasValue) {
            InstallInterruptWithCallback(callback.Index, callback.InterruptHandlerSegment.Value, 0x0000);
        } else {
            _offset += InstallInterruptWithCallback(callback.Index, _callbackHandlerSegment, _offset);
        }
    }

    /// <summary>
    /// Installs an interrupt vector in the vector table and sets up a callback function to be called when the interrupt is triggered.
    /// </summary>
    /// <param name="vectorNumber">The interrupt vector number to install.</param>
    /// <param name="segment">The segment address of the callback function.</param>
    /// <param name="offset">The offset address of the callback function.</param>
    /// <returns>The size of the callback function.</returns>
    private ushort InstallInterruptWithCallback(byte vectorNumber, ushort segment, ushort offset) {
        InstallVectorInTable(vectorNumber, segment, offset);
        return WriteInterruptCallback(vectorNumber, segment, offset);
    }

    /// <summary>
    /// Installs an interrupt vector in the vector table.
    /// </summary>
    /// <param name="vectorNumber">The interrupt vector number to install.</param>
    /// <param name="segment">The segment address of the vector.</param>
    /// <param name="offset">The offset address of the vector.</param>
    private void InstallVectorInTable(byte vectorNumber, ushort segment, ushort offset) {
        // install the vector in the vector table
        _memory.SetUint16((ushort)((4 * vectorNumber) + 2), segment);
        _memory.SetUint16((ushort)(4 * vectorNumber), offset);
    }

    /// <summary>
    /// Writes the callback function for an interrupt vector to memory.
    /// </summary>
    /// <param name="vectorNumber">The interrupt vector number to write the callback for.</param>
    /// <param name="segment">The segment address of the callback function.</param>
    /// <param name="offset">The offset address of the callback function.</param>
    /// <returns>The size of the callback function.</returns>
    private ushort WriteInterruptCallback(byte vectorNumber, ushort segment, ushort offset) {
        _callbackAddresses.Add(vectorNumber, new SegmentedAddress(segment, offset));
        uint address = MemoryUtils.ToPhysicalAddress(segment, offset);

        // CALLBACK opcode (custom instruction, FE38 + 16 bits callback number)
        _memory.SetUint8(address, 0xFE);
        _memory.SetUint8(address + 1, 0x38);

        // vector to call
        _memory.SetUint8(address + 2, vectorNumber);

        // IRET
        _memory.SetUint8(address + 3, 0xCF);

        // 4 bytes used
        return CallbackSize;
    }
}