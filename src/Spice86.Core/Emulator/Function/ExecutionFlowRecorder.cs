﻿namespace Spice86.Core.Emulator.Function;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Utils;

/// <summary>
/// A class that records machine code execution flow.
/// </summary>
public class ExecutionFlowRecorder : IExecutionDumpFactory {
    /// <summary>
    /// Gets or sets whether we register calls, jumps, returns, and unaligned returns.
    /// </summary>
    public bool RecordData { get; set; }
    
    /// <summary>
    /// Gets or sets whether we register self modifying machine code.
    /// </summary>
    public bool IsRegisterExecutableCodeModificationEnabled { get; set; } = true;


    private readonly ExecutionDump _executionDump;

    private readonly HashSet<ulong> _callsEncountered = new(200000);

    private readonly HashSet<ulong> _jumpsEncountered = new(200000);

    private readonly HashSet<ulong> _retsEncountered = new(200000);

    private readonly HashSet<ulong> _unalignedRetsEncountered = new(200000);

    private readonly HashSet<uint> _instructionsEncountered = new(200000);

    private readonly HashSet<uint> _executableCodeAreasEncountered = new(200000);

    private readonly CircularBuffer<CallRecord> _functionCalls = new(20);
    private ushort _callDepth;

    /// <summary>
    /// Initializes a new instance. <see cref="RecordData"/> is set to false.
    /// </summary>
    public ExecutionFlowRecorder(bool recordData, ExecutionDump executionDump) {
        RecordData = recordData;
        _executionDump = executionDump;
    }

    public ExecutionDump Dump() {
        return _executionDump;
    }

    /// <summary>
    /// Registers a call from one address to another.
    /// </summary>
    /// <param name="fromCS">The segment of the address making the call.</param>
    /// <param name="fromIP">The offset of the address making the call.</param>
    /// <param name="toCS">The segment of the address being called.</param>
    /// <param name="toIP">The offset of the address being called.</param>
    public void RegisterCall(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        RegisterAddressJump(_executionDump.CallsFromTo, _callsEncountered, fromCS, fromIP, toCS, toIP);
#if DEBUG
        _functionCalls.Add(new CallRecord(_callDepth++, fromCS, fromIP, toCS, toIP));
#endif
    }

    /// <summary>
    /// Registers a jump from one address to another.
    /// </summary>
    /// <param name="fromCS">The segment of the address making the call.</param>
    /// <param name="fromIP">The offset of the address making the call.</param>
    /// <param name="toCS">The segment of the address being called.</param>
    /// <param name="toIP">The offset of the address being called.</param>
    public void RegisterJump(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        RegisterAddressJump(_executionDump.JumpsFromTo, _jumpsEncountered, fromCS, fromIP, toCS, toIP);
    }

    /// <summary>
    /// Registers a return from one address to another.
    /// </summary>
    /// <param name="fromCS">The segment of the address making the call.</param>
    /// <param name="fromIP">The offset of the address making the call.</param>
    /// <param name="toCS">The segment of the address being called.</param>
    /// <param name="toIP">The offset of the address being called.</param>
    public void RegisterReturn(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        RegisterAddressJump(_executionDump.RetsFromTo, _retsEncountered, fromCS, fromIP, toCS, toIP);
        _callDepth--;
    }

    /// <summary>
    /// Registers an unaligned return from one address to another.
    /// </summary>
    /// <param name="fromCS">The segment of the address making the call.</param>
    /// <param name="fromIP">The offset of the address making the call.</param>
    /// <param name="toCS">The segment of the address being called.</param>
    /// <param name="toIP">The offset of the address being called.</param>
    public void RegisterUnalignedReturn(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        RegisterAddressJump(_executionDump.UnalignedRetsFromTo, _unalignedRetsEncountered, fromCS, fromIP, toCS, toIP);
    }

    /// <summary>
    /// Registers executed CPU instruction.
    /// </summary>
    /// <param name="cs">The segment.</param>
    /// <param name="ip">The offset.</param>
    public void RegisterExecutedInstruction(ushort cs, ushort ip) {
        if (!AddSegmentedAddressInCache(_instructionsEncountered, cs, ip)) {
            return;
        }

        _executionDump.ExecutedInstructions.Add(new SegmentedAddress(cs, ip));
    }

    /// <summary>
    /// Add the segmented address in the cache.
    /// </summary>
    /// <param name="cache">The cache to add the segmented address to.</param>
    /// <param name="segment">The address segment.</param>
    /// <param name="offset">The address offset.</param>
    /// <returns><c>true</c> when the address was added, <c>false</c> if it was already there</returns>
    private static bool AddSegmentedAddressInCache(HashSet<uint> cache, ushort segment, ushort offset) {
        return cache.Add(MemoryUtils.ToPhysicalAddress(segment, offset));
    }

    /// <summary>
    /// Creates a memory write breakpoint on the given executable address.
    /// When triggered will fill <see cref="ExecutionDump.ExecutableAddressWrittenBy"/> appropriately:
    ///  - key of the map is the address being modified
    ///  - value is a dictionary of instruction addresses that modified it, with for each instruction a list of the before and after values.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="emulatorBreakpointsManager">The class used to store breakpoints.</param>
    /// <param name="cs">The value of the CS register, for the segment.</param>
    /// <param name="ip">The value of the IP register, for the offset.</param>
    public void RegisterExecutableByte(IMemory memory, State state, EmulatorBreakpointsManager emulatorBreakpointsManager, ushort cs, ushort ip) {
        // Note: this is not enough, instructions modified before they are discovered are not counted as rewritten.
        // If we saved the coverage to reload it each time, we would get a different picture of the rewritten code but that would come with other issues.
        // Code modified before being ever executed is arguably not self modifying code. 
        uint address = MemoryUtils.ToPhysicalAddress(cs, ip);
        RegisterExecutableByteModificationBreakPoint(memory, state, emulatorBreakpointsManager, address);
    }

    /// <summary>
    /// Creates a memory write breakpoint on the given executable address.
    /// When triggered will fill <see cref="ExecutionDump.ExecutableAddressWrittenBy"/> appropriately:<br/>
    ///  - key of the map is the address being modified <br/>
    ///  - value is a dictionary of instruction addresses that modified it, with for each instruction a list of the before and after values.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="emulatorBreakpointsManager">The class that stores emulation breakpoints.</param>
    /// <param name="physicalAddress">The address to set the breakpoint at.</param>
    public void RegisterExecutableByteModificationBreakPoint(IMemory memory, State state, EmulatorBreakpointsManager emulatorBreakpointsManager, uint physicalAddress) {
        if (!_executableCodeAreasEncountered.Add(physicalAddress)) {
            return;
        }

        AddressBreakPoint? breakPoint;
        breakPoint = GenerateBreakPoint(memory, state, physicalAddress);

        emulatorBreakpointsManager.ToggleBreakPoint(breakPoint, true);
    }

    private AddressBreakPoint GenerateBreakPoint(IMemory memory, State state, uint physicalAddress) {
        AddressBreakPoint breakPoint = new(BreakPointType.MEMORY_WRITE, physicalAddress, _ => {
            if (!IsRegisterExecutableCodeModificationEnabled) {
                return;
            }

            byte oldValue = memory.UInt8[physicalAddress];
            byte newValue = memory.CurrentlyWritingByte;
            if (oldValue != newValue) {
                RegisterExecutableByteModification(
                    new SegmentedAddress(state.CS, state.IP), physicalAddress, oldValue, newValue);
            }
        }, false);
        return breakPoint;
    }

    private void RegisterExecutableByteModification(SegmentedAddress instructionAddress, uint modifiedAddress, byte oldValue, byte newValue) {
        uint instructionAddressPhysical = instructionAddress.Linear;
        if (instructionAddressPhysical == 0) {
            // Probably Exe load
            return;
        }
        if (!_executionDump.ExecutableAddressWrittenBy.TryGetValue(modifiedAddress,
                out IDictionary<uint, HashSet<ByteModificationRecord>>? instructionsChangingThisAddress)) {
            instructionsChangingThisAddress = new Dictionary<uint, HashSet<ByteModificationRecord>>();
            _executionDump.ExecutableAddressWrittenBy[modifiedAddress] = instructionsChangingThisAddress;
        }
        if (!instructionsChangingThisAddress.TryGetValue(instructionAddressPhysical, out HashSet<ByteModificationRecord>? byteModificationRecords)) {
            byteModificationRecords = new HashSet<ByteModificationRecord>();
            instructionsChangingThisAddress[instructionAddressPhysical] = byteModificationRecords;
        }
        byteModificationRecords.Add(new ByteModificationRecord(oldValue, newValue));
    }

    private void RegisterAddressJump(IDictionary<uint, HashSet<SegmentedAddress>> FromTo, HashSet<ulong> encountered, ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        if (!RecordData) {
            return;
        }
        ulong key = fromCS | (ulong)fromIP << 16 | (ulong)toCS << 32 | (ulong)toIP << 48;
        if (encountered.Contains(key)) {
            return;
        }
        encountered.Add(key);
        uint physicalFrom = MemoryUtils.ToPhysicalAddress(fromCS, fromIP);
        if (!FromTo.TryGetValue(physicalFrom, out HashSet<SegmentedAddress>? destinationAddresses)) {
            destinationAddresses = new HashSet<SegmentedAddress>();
            FromTo.Add(physicalFrom, destinationAddresses);
        }
        destinationAddresses.Add(new SegmentedAddress(toCS, toIP));
    }

    /// <summary>
    /// Create an overview of the function call flow of the last X function calls.
    /// </summary>
    /// <returns></returns>
    public string DumpFunctionCalls() {
        return $"Address -> called function\n{_functionCalls}";
    }

    private readonly record struct CallRecord(ushort Depth, ushort FromCs, ushort FromIp, ushort ToCs, ushort ToIp) {
        public override string ToString() => $"{new string('.', Depth)}{FromCs:X4}:{FromIp:X4} -> {ToCs:X4}:{ToIp:X4}";
    }
}
