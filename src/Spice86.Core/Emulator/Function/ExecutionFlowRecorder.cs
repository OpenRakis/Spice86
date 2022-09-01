namespace Spice86.Core.Emulator.Function;

using Memory;

using System.Collections.Generic;

public class ExecutionFlowRecorder {
    public bool RecordData { set; private get; }
    public IDictionary<uint, ISet<SegmentedAddress>> CallsFromTo { get; }
    private readonly ISet<ulong> _callsEncountered = new HashSet<ulong>();
    public IDictionary<uint, ISet<SegmentedAddress>> JumpsFromTo { get; }
    private readonly ISet<ulong> _jumpsEncountered = new HashSet<ulong>();
    public IDictionary<uint, ISet<SegmentedAddress>> RetsFromTo { get; }
    private readonly ISet<ulong> _retsEncountered = new HashSet<ulong>();

    // modified byte address -> dictionary of modifying instructions with for each instruction a set of the possible changes the instruction did
    public IDictionary<uint, IDictionary<uint, ISet<ByteModificationRecord>>> ExecutableAddressWrittenBy { get; }

    public ExecutionFlowRecorder() {
        RecordData = false;
        CallsFromTo = new Dictionary<uint, ISet<SegmentedAddress>>();
        JumpsFromTo = new Dictionary<uint, ISet<SegmentedAddress>>();
        RetsFromTo = new Dictionary<uint, ISet<SegmentedAddress>>();
        ExecutableAddressWrittenBy = new Dictionary<uint, IDictionary<uint, ISet<ByteModificationRecord>>>();
    }

    public void RegisterCall(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        RegisterAddressJump(CallsFromTo, _callsEncountered, fromCS, fromIP, toCS, toIP);
    }

    public void RegisterJump(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        RegisterAddressJump(JumpsFromTo, _jumpsEncountered, fromCS, fromIP, toCS, toIP);
    }

    public void RegisterReturn(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        RegisterAddressJump(RetsFromTo, _retsEncountered, fromCS, fromIP, toCS, toIP);
    }

    public void RegisterExecutableCodeModification(SegmentedAddress instructionAddress, uint modifiedAddress, byte oldValue, byte newValue) {
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