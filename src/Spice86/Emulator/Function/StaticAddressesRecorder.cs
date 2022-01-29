namespace Spice86.Emulator.Function;

using Spice86.Emulator.CPU;
using Spice86.Emulator.Memory;

using System;
using System.Collections.Generic;

public class StaticAddressesRecorder {
    private readonly bool _debugMode;

    private readonly Dictionary<uint, string> _names = new();

    private readonly Dictionary<uint, SegmentRegisterBasedAddress> _segmentRegisterBasedAddress = new();

    private readonly SegmentRegisters _segmentRegisters;

    private readonly HashSet<SegmentedAddress> _whiteListOfSegmentForOffset = new();

    private AddressOperation? _currentAddressOperation;

    private ushort? _currentOffset;

    private int? _currentSegmentIndex;

    public StaticAddressesRecorder(State state, bool debugMode) {
        this._debugMode = debugMode;
        this._segmentRegisters = state.GetSegmentRegisters();
    }

    public void AddName(uint physicalAddress, string name) {
        _names.Add(physicalAddress, name);
    }

    public void AddSegmentTowhiteList(SegmentedAddress address) {
        _whiteListOfSegmentForOffset.Add(address);
    }

    public void Commit() {
        if (_debugMode && _currentSegmentIndex != null && _currentOffset != null && _currentAddressOperation != null && _currentSegmentIndex != null) {
            ushort segmentValue = _segmentRegisters.GetRegister(_currentSegmentIndex.Value);
            uint physicalAddress = MemoryUtils.ToPhysicalAddress(segmentValue, _currentOffset.Value);
            if (_segmentRegisterBasedAddress.TryGetValue(physicalAddress, out SegmentRegisterBasedAddress? value) == false) {
                value = new SegmentRegisterBasedAddress(segmentValue, _currentOffset.Value, _names[physicalAddress]);
                _segmentRegisterBasedAddress.Add(physicalAddress, value);
            }
            value.AddAddressOperation(_currentAddressOperation, _currentSegmentIndex.Value);
        }
    }

    public Dictionary<uint, String> GetNames() {
        return _names;
    }

    public ICollection<SegmentRegisterBasedAddress> GetSegmentRegisterBasedAddress() {
        return _segmentRegisterBasedAddress.Values;
    }

    public HashSet<SegmentedAddress> GetWhiteListOfSegmentForOffset() {
        return _whiteListOfSegmentForOffset;
    }

    public void Reset() {
        _currentSegmentIndex = null;
        _currentOffset = null;
        _currentAddressOperation = null;
    }

    public void SetCurrentAddressOperation(ValueOperation valueOperation, OperandSize operandSize) {
        _currentAddressOperation = new AddressOperation(valueOperation, operandSize);
    }

    public void SetCurrentValue(int regIndex, ushort offset) {
        _currentSegmentIndex = regIndex;
        _currentOffset = offset;
    }
}