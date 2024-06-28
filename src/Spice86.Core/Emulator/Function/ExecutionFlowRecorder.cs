namespace Spice86.Core.Emulator.Function;

using Newtonsoft.Json;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// A class that records machine code execution flow.
/// </summary>
public class ExecutionFlowRecorder {
    /// <summary>
    /// Gets or sets whether we register calls, jumps, returns, and unaligned returns.
    /// </summary>
    public bool RecordData { get; set; }

    /// <summary>
    /// Gets a dictionary of calls from one address to another.
    /// </summary>
    public IDictionary<uint, HashSet<SegmentedAddress>> CallsFromTo { get; set; }
    private readonly HashSet<ulong> _callsEncountered = new(200000);
    /// <summary>
    /// Gets a dictionary of jumps from one address to another.
    /// </summary>
    public IDictionary<uint, HashSet<SegmentedAddress>> JumpsFromTo { get; set; }
    private readonly HashSet<ulong> _jumpsEncountered = new(200000);
    /// <summary>
    /// Gets a dictionary of returns from one address to another.
    /// </summary>
    public IDictionary<uint, HashSet<SegmentedAddress>> RetsFromTo { get; set; }
    private readonly HashSet<ulong> _retsEncountered = new(200000);
    /// <summary>
    /// Gets a dictionary of unaligned returns from one address to another.
    /// </summary>
    public IDictionary<uint, HashSet<SegmentedAddress>> UnalignedRetsFromTo { get; set; }
    private readonly HashSet<ulong> _unalignedRetsEncountered = new(200000);
    /// <summary>
    /// Gets the set of executed instructions.
    /// </summary>
    public HashSet<SegmentedAddress> ExecutedInstructions { get; set; }
    private readonly HashSet<uint> _instructionsEncountered = new(200000);
    private readonly HashSet<uint> _executableCodeAreasEncountered = new(200000);

    private readonly CircularBuffer<string> _callStack = new(20);

    /// <summary>
    /// Gets or sets whether we register self modifying machine code.
    /// </summary>
    [JsonIgnore]
    public bool IsRegisterExecutableCodeModificationEnabled { get; set; } = true;

    /// <summary>
    /// Gets a dictionary of executable addresses written by modifying instructions.
    /// The key of the outer dictionary is the modified byte address.
    /// The value of the outer dictionary is a dictionary of modifying instructions, where the key is the instruction address and the value is a set of possible changes that the instruction did.
    /// </summary>
    public IDictionary<uint, IDictionary<uint, HashSet<ByteModificationRecord>>> ExecutableAddressWrittenBy { get; }

    /// <summary>
    /// Initializes a new instance. <see cref="RecordData"/> is set to false.
    /// </summary>
    public ExecutionFlowRecorder() {
        RecordData = false;
        CallsFromTo = new Dictionary<uint, HashSet<SegmentedAddress>>(200000);
        JumpsFromTo = new Dictionary<uint, HashSet<SegmentedAddress>>(200000);
        RetsFromTo = new Dictionary<uint, HashSet<SegmentedAddress>>(200000);
        UnalignedRetsFromTo = new Dictionary<uint, HashSet<SegmentedAddress>>(200000);
        ExecutedInstructions = new HashSet<SegmentedAddress>();
        ExecutableAddressWrittenBy = new Dictionary<uint, IDictionary<uint, HashSet<ByteModificationRecord>>>(200000);
    }

    /// <summary>
    /// Registers a call from one address to another.
    /// </summary>
    /// <param name="fromCS">The segment of the address making the call.</param>
    /// <param name="fromIP">The offset of the address making the call.</param>
    /// <param name="toCS">The segment of the address being called.</param>
    /// <param name="toIP">The offset of the address being called.</param>
    public void RegisterCall(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        RegisterAddressJump(CallsFromTo, _callsEncountered, fromCS, fromIP, toCS, toIP);
#if DEBUG
        _callStack.Add($"{fromCS:X4}:{fromIP:X4} -> {toCS:X4}:{toIP:X4}");
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
        RegisterAddressJump(JumpsFromTo, _jumpsEncountered, fromCS, fromIP, toCS, toIP);
    }

    /// <summary>
    /// Registers a return from one address to another.
    /// </summary>
    /// <param name="fromCS">The segment of the address making the call.</param>
    /// <param name="fromIP">The offset of the address making the call.</param>
    /// <param name="toCS">The segment of the address being called.</param>
    /// <param name="toIP">The offset of the address being called.</param>
    public void RegisterReturn(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        RegisterAddressJump(RetsFromTo, _retsEncountered, fromCS, fromIP, toCS, toIP);
    }

    /// <summary>
    /// Registers an unaligned return from one address to another.
    /// </summary>
    /// <param name="fromCS">The segment of the address making the call.</param>
    /// <param name="fromIP">The offset of the address making the call.</param>
    /// <param name="toCS">The segment of the address being called.</param>
    /// <param name="toIP">The offset of the address being called.</param>
    public void RegisterUnalignedReturn(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        RegisterAddressJump(UnalignedRetsFromTo, _unalignedRetsEncountered, fromCS, fromIP, toCS, toIP);
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

        ExecutedInstructions.Add(new SegmentedAddress(cs, ip));
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
    /// When triggered will fill <see cref="ExecutableAddressWrittenBy"/> appropriately:
    ///  - key of the map is the address being modified
    ///  - value is a dictionary of instruction addresses that modified it, with for each instruction a list of the before and after values.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="machineBreakpoints">The class used to store breakpoints.</param>
    /// <param name="cs">The value of the CS register, for the segment.</param>
    /// <param name="ip">The value of the IP register, for the offset.</param>
    public void RegisterExecutableByte(IMemory memory, State state, MachineBreakpoints machineBreakpoints, ushort cs, ushort ip) {
        // Note: this is not enough, instructions modified before they are discovered are not counted as rewritten.
        // If we saved the coverage to reload it each time, we would get a different picture of the rewritten code but that would come with other issues.
        // Code modified before being ever executed is arguably not self modifying code. 
        uint address = MemoryUtils.ToPhysicalAddress(cs, ip);
        RegisterExecutableByteModificationBreakPoint(memory, state, machineBreakpoints, address);
    }

    /// <summary>
    /// Creates a memory write breakpoint on the given executable address.
    /// When triggered will fill <see cref="ExecutableAddressWrittenBy"/> appropriately:<br/>
    ///  - key of the map is the address being modified <br/>
    ///  - value is a dictionary of instruction addresses that modified it, with for each instruction a list of the before and after values.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="machineBreakpoints">The class that stores emulation breakpoints.</param>
    /// <param name="physicalAddress">The address to set the breakpoint at.</param>
    public void RegisterExecutableByteModificationBreakPoint(IMemory memory, State state, MachineBreakpoints machineBreakpoints, uint physicalAddress) {
        if (!_executableCodeAreasEncountered.Add(physicalAddress)) {
            return;
        }

        AddressBreakPoint? breakPoint;
        breakPoint = GenerateBreakPoint(memory, state, physicalAddress);

        machineBreakpoints.ToggleBreakPoint(breakPoint, true);
    }

    private AddressBreakPoint GenerateBreakPoint(IMemory memory, State state, uint physicalAddress) {
        AddressBreakPoint breakPoint = new(BreakPointType.WRITE, physicalAddress, _ => {
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
        uint instructionAddressPhysical = instructionAddress.ToPhysical();
        if (instructionAddressPhysical == 0) {
            // Probably Exe load
            return;
        }
        if (!ExecutableAddressWrittenBy.TryGetValue(modifiedAddress,
                out IDictionary<uint, HashSet<ByteModificationRecord>>? instructionsChangingThisAddress)) {
            instructionsChangingThisAddress = new Dictionary<uint, HashSet<ByteModificationRecord>>();
            ExecutableAddressWrittenBy[modifiedAddress] = instructionsChangingThisAddress;
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
    /// Lists the current call stack.
    /// </summary>
    /// <returns></returns>
    public string DumpCallStack() {
        return _callStack.ToString();
    }
}