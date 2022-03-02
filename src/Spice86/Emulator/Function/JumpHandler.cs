namespace Spice86.Emulator.Function;

using Memory;

using System.Collections.Generic;

public class JumpHandler {
    public bool DebugMode { set; private get; }
    public IDictionary<uint, ISet<SegmentedAddress>> CallsFromTo { get; }
    private ISet<ulong> _callsEncountered = new HashSet<ulong>();
    public IDictionary<uint, ISet<SegmentedAddress>> JumpsFromTo { get; }
    private ISet<ulong> _jumpsEncountered = new HashSet<ulong>();
    public IDictionary<uint, ISet<SegmentedAddress>> RetsFromTo { get; }
    private ISet<ulong> _retsEncountered = new HashSet<ulong>();

    public JumpHandler() {
        DebugMode = false;
        CallsFromTo = new Dictionary<uint, ISet<SegmentedAddress>>();
        JumpsFromTo = new Dictionary<uint, ISet<SegmentedAddress>>();
        RetsFromTo = new Dictionary<uint, ISet<SegmentedAddress>>();
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

    private void RegisterAddressJump(IDictionary<uint, ISet<SegmentedAddress>> FromTo, ISet<ulong> encountered, ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        if (!DebugMode) {
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