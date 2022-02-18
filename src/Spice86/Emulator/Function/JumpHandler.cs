namespace Spice86.Emulator.Function;

using Memory;

using System.Collections.Generic;

public class JumpHandler {
    public bool DebugMode { set; private get; }
    public IDictionary<uint, ISet<uint>> CallsFromTo { get; }
    public IDictionary<uint, ISet<uint>> JumpsFromTo { get; }
    public IDictionary<uint, ISet<uint>> RetsFromTo { get; }

    public JumpHandler() {
        DebugMode = false;
        CallsFromTo = new Dictionary<uint, ISet<uint>>();
        JumpsFromTo = new Dictionary<uint, ISet<uint>>();
        RetsFromTo = new Dictionary<uint, ISet<uint>>();
    }

    public void RegisterCall(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        RegisterAddressJump(CallsFromTo, fromCS, fromIP, toCS, toIP);
    }

    public void RegisterJump(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        RegisterAddressJump(JumpsFromTo, fromCS, fromIP, toCS, toIP);
    }

    public void RegisterReturn(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        RegisterAddressJump(RetsFromTo, fromCS, fromIP, toCS, toIP);
    }

    private void RegisterAddressJump(IDictionary<uint, ISet<uint>> FromTo, ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        if (!DebugMode) {
            return;
        }
        uint physicalFrom = MemoryUtils.ToPhysicalAddress(fromCS, fromIP);
        uint physicalTo = MemoryUtils.ToPhysicalAddress(toCS, toIP);

        if (!FromTo.TryGetValue(physicalFrom, out ISet<uint>? destinationAddresses)) {
            destinationAddresses = new HashSet<uint>();
            FromTo.Add(physicalFrom, destinationAddresses);
        }
        destinationAddresses.Add(physicalTo);
    }
}