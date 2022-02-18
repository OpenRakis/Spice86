namespace Spice86.Emulator.Function;

using Memory;

using System.Collections.Generic;

public class JumpHandler {
    private readonly JumpRecorder _callsFromToRecorder;
    private readonly JumpRecorder _jumpsFromToRecorder;
    private readonly JumpRecorder _retsFromToRecorder;
    public IDictionary<SegmentedAddress, ISet<SegmentedAddress>> CallsFromTo => _callsFromToRecorder.FromTo;
    public IDictionary<SegmentedAddress, ISet<SegmentedAddress>> JumpsFromTo => _jumpsFromToRecorder.FromTo;
    public IDictionary<SegmentedAddress, ISet<SegmentedAddress>> RetsFromTo => _retsFromToRecorder.FromTo;

    public JumpHandler(bool debugMode) {
        _callsFromToRecorder = new JumpRecorder(debugMode);
        _jumpsFromToRecorder = new JumpRecorder(debugMode);
        _retsFromToRecorder = new JumpRecorder(debugMode);
    }

    public void RegisterCall(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        _callsFromToRecorder.RegisterAddressJump(fromCS, fromIP, toCS, toIP);
    }

    public void RegisterJump(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        _jumpsFromToRecorder.RegisterAddressJump(fromCS, fromIP, toCS, toIP);
    }

    public void RegisterReturn(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        _retsFromToRecorder.RegisterAddressJump(fromCS, fromIP, toCS, toIP);
    }
}

class JumpRecorder {
    private readonly bool _debugMode;
    /// <summary>
    /// This is here for performance, in order to avoid creating new objects as this is called all the time.
    /// </summary>
    private readonly ISet<ulong> _registeredPairs = new HashSet<ulong>();
    public IDictionary<SegmentedAddress, ISet<SegmentedAddress>> FromTo { get; }
    public JumpRecorder(bool debugMode) {
        _debugMode = debugMode;
        FromTo = new Dictionary<SegmentedAddress, ISet<SegmentedAddress>>();
    }
    public void RegisterAddressJump(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        if (!_debugMode) {
            return;
        }
        ulong registeredPairsKey = toRegisteredPairsKey(fromCS, fromIP, toCS, toIP);
        if (_registeredPairs.Contains(registeredPairsKey)) {
            // This is here for performance reasons only
            return;
        }
        _registeredPairs.Add(registeredPairsKey);
        SegmentedAddress from = new SegmentedAddress(fromCS, fromIP);
        SegmentedAddress to = new SegmentedAddress(toCS, toIP);
        if (!FromTo.TryGetValue(from, out ISet<SegmentedAddress>? destinationAddresses)) {
            destinationAddresses = new HashSet<SegmentedAddress>();
            FromTo.Add(from, destinationAddresses);
        }
        destinationAddresses.Add(to);
    }
    private ulong toRegisteredPairsKey(ushort fromCS, ushort fromIP, ushort toCS, ushort toIP) {
        return fromCS | ((ulong)fromIP) << 16 | ((ulong)toCS) << 32 | ((ulong)toIP) << 48;
    }
}