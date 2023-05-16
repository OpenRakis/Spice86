namespace Spice86.Core.Emulator.Function;

using Newtonsoft.Json;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// A class that records machine code execution flow.
/// </summary>
public class ExecutionFlowRecorder {
    /// <summary>
    /// Gets or sets whether we register calls, jumps, returns, and unaligned returns.
    /// </summary>
    public bool RecordData { set; get; }
    
    /// <summary>
    /// Gets a dictionary of calls from one address to another.
    /// </summary>
    public IDictionary<uint, ISet<SegmentedAddress>> CallsFromTo { get; }
    private readonly ISet<ulong> _callsEncountered = new HashSet<ulong>();
    
    /// <summary>
    /// Gets a dictionary of jumps from one address to another.
    /// </summary>
    public IDictionary<uint, ISet<SegmentedAddress>> JumpsFromTo { get; }
    private readonly ISet<ulong> _jumpsEncountered = new HashSet<ulong>();
    
    /// <summary>
    /// Gets a dictionary of returns from one address to another.
    /// </summary>
    public IDictionary<uint, ISet<SegmentedAddress>> RetsFromTo { get; }
    private readonly ISet<ulong> _retsEncountered = new HashSet<ulong>();
    
    /// <summary>
    /// Gets a dictionary of unaligned returns from one address to another.
    /// </summary>
    public IDictionary<uint, ISet<SegmentedAddress>> UnalignedRetsFromTo { get; }
    private readonly ISet<ulong> _unalignedRetsEncountered = new HashSet<ulong>();
    
    /// <summary>
    /// Gets the set of executed instructions.
    /// </summary>
    public ISet<SegmentedAddress> ExecutedInstructions { get; }
    private readonly ISet<uint> _instructionsEncountered = new HashSet<uint>();
    private readonly ISet<uint> _executableCodeAreasEncountered = new HashSet<uint>();

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
    public IDictionary<uint, IDictionary<uint, ISet<ByteModificationRecord>>> ExecutableAddressWrittenBy { get; }

    /// <summary>
    /// Initializes a new instance. <see cref="RecordData"/> is set to false.
    /// </summary>
    public ExecutionFlowRecorder() {
        RecordData = false;
        CallsFromTo = new Dictionary<uint, ISet<SegmentedAddress>>();
        JumpsFromTo = new Dictionary<uint, ISet<SegmentedAddress>>();
        RetsFromTo = new Dictionary<uint, ISet<SegmentedAddress>>();
        UnalignedRetsFromTo = new Dictionary<uint, ISet<SegmentedAddress>>();
        ExecutedInstructions = new HashSet<SegmentedAddress>();
        ExecutableAddressWrittenBy = new Dictionary<uint, IDictionary<uint, ISet<ByteModificationRecord>>>();
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
    private static bool AddSegmentedAddressInCache(ISet<uint> cache, ushort segment, ushort offset) {
        return cache.Add(MemoryUtils.ToPhysicalAddress(segment, offset));
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="cs">The value of the CS register, for the segment.</param>
    /// <param name="ip">The value of the IP register, for the offset.</param>
    public void RegisterExecutableByte(Machine machine, ushort cs, ushort ip) {
        // Note: this is not enough, instructions modified before they are discovered are not counted as rewritten.
        // If we saved the coverage to reload it each time, we would get a different picture of the rewritten code but that would come with other issues.
        // Code modified before being ever executed is arguably not self modifying code. 
        uint address = MemoryUtils.ToPhysicalAddress(cs, ip);
        RegisterExecutableByteModificationBreakPoint(machine, address);
    }

    /// <summary>
    /// Creates a memory write breakpoint on the given executable address.
    /// When triggered will fill <see cref="ExecutableAddressWrittenBy"/> appropriately:
    ///  - key of the map is the address being modified
    ///  - value is a dictionary of instruction addresses that modified it, with for each instruction a list of the before and after values.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="physicalAddress">The address to set the breakpoint at.</param>
    public void RegisterExecutableByteModificationBreakPoint(Machine machine, uint physicalAddress) {
        if (!_executableCodeAreasEncountered.Add(physicalAddress)) {
            return;
        }

        AddressBreakPoint breakPoint = new(BreakPointType.WRITE, physicalAddress, _ => {
            if (!IsRegisterExecutableCodeModificationEnabled) {
                return;
            }

            byte oldValue = machine.Memory.UInt8[physicalAddress];
            byte newValue = machine.Memory.CurrentlyWritingByte;
            if (oldValue != newValue) {
                State state = machine.Cpu.State;
                RegisterExecutableByteModification(
                    new SegmentedAddress(state.CS, state.IP), physicalAddress, oldValue, newValue);
            }
        }, false);
        machine.MachineBreakpoints.ToggleBreakPoint(breakPoint, true);
    }

    private void RegisterExecutableByteModification(SegmentedAddress instructionAddress, uint modifiedAddress, byte oldValue, byte newValue) {
        uint instructionAddressPhysical = instructionAddress.ToPhysical();
        if (instructionAddressPhysical == 0) {
            // Probably Exe load
            return;
        }
        if (!ExecutableAddressWrittenBy.TryGetValue(modifiedAddress,
                out IDictionary<uint, ISet<ByteModificationRecord>>? instructionsChangingThisAddress)) {
            instructionsChangingThisAddress = new Dictionary<uint, ISet<ByteModificationRecord>>();
            ExecutableAddressWrittenBy[modifiedAddress] = instructionsChangingThisAddress;
        }
        if (!instructionsChangingThisAddress.TryGetValue(instructionAddressPhysical, out ISet<ByteModificationRecord>? byteModificationRecords)) {
            byteModificationRecords = new HashSet<ByteModificationRecord>();
            instructionsChangingThisAddress[instructionAddressPhysical] = byteModificationRecords;
        }
        byteModificationRecords.Add(new ByteModificationRecord(oldValue, newValue));
    }

    private void RegisterAddressJump(IDictionary<uint, ISet<SegmentedAddress>> FromTo, ISet<ulong> encountered, ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        if (!RecordData) {
            return;
        }
        ulong key = fromCS | (ulong)fromIP << 16 | (ulong)toCS << 32 | (ulong)toIP << 48;
        if (encountered.Contains(key)) {
            return;
        }
        encountered.Add(key);
        uint physicalFrom = MemoryUtils.ToPhysicalAddress(fromCS, fromIP);
        if (!FromTo.TryGetValue(physicalFrom, out ISet<SegmentedAddress>? destinationAddresses)) {
            destinationAddresses = new HashSet<SegmentedAddress>();
            FromTo.Add(physicalFrom, destinationAddresses);
        }
        destinationAddresses.Add(new SegmentedAddress(toCS, toIP));
    }
}