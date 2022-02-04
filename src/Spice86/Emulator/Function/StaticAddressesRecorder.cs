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

    private readonly ISet<SegmentedAddress> _whiteListOfSegmentForOffset = new HashSet<SegmentedAddress>();

    private ValueOperation? CurrentValueOperation { get; set; }
    private OperandSize? CurrentOperandSize { get; set; }

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
        if (_debugMode && _currentSegmentIndex != null && _currentOffset != null && CurrentValueOperation != null && CurrentOperandSize!=null && _currentSegmentIndex != null) {
            ushort segmentValue = _segmentRegisters.GetRegister(_currentSegmentIndex.Value);
            uint physicalAddress = MemoryUtils.ToPhysicalAddress(segmentValue, _currentOffset.Value);
            if (_segmentRegisterBasedAddress.TryGetValue(physicalAddress, out SegmentRegisterBasedAddress? value) == false) {
                value = new SegmentRegisterBasedAddress(segmentValue, _currentOffset.Value, _names[physicalAddress]);
                _segmentRegisterBasedAddress.Add(physicalAddress, value);
            }
            AddressOperation currentAddressOperation = new((ValueOperation)CurrentValueOperation, CurrentOperandSize);
            value.AddAddressOperation(currentAddressOperation, _currentSegmentIndex.Value);
        }
    }

    public Dictionary<uint, String> GetNames() {
        return _names;
    }

    public ICollection<SegmentRegisterBasedAddress> GetSegmentRegisterBasedAddress() {
        return _segmentRegisterBasedAddress.Values;
    }

    public ISet<SegmentedAddress> GetWhiteListOfSegmentForOffset() {
        return _whiteListOfSegmentForOffset;
    }

    public void Reset() {
        _currentSegmentIndex = null;
        _currentOffset = null;
        CurrentValueOperation = null;
        CurrentOperandSize = null;
    }

    public void SetCurrentAddressOperation(ValueOperation valueOperation, OperandSize operandSize) {
        CurrentValueOperation = valueOperation;
        CurrentOperandSize = operandSize;
    }

    public void SetCurrentValue(int regIndex, ushort offset) {
        _currentSegmentIndex = regIndex;
        _currentOffset = offset;
    }
}