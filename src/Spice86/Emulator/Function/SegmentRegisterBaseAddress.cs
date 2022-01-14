namespace Spice86.Emulator.Function;

using Spice86.Emulator.Memory;

using System.Collections.Generic;

public class SegmentRegisterBasedAddress : SegmentedAddress {
    private readonly Dictionary<AddressOperation, List<int>> _addressOperations = new();

    private readonly string _name;

    public SegmentRegisterBasedAddress(int segment, int offset, string name) : base(segment, offset) {
        this._name = name;
    }

    public void AddAddressOperation(AddressOperation addressOperation, int segmentRegisterIndex) {
        List<int> segmentRegisterIndexes = _addressOperations.ComputeIfAbsent(addressOperation, () => new());
        segmentRegisterIndexes.Add(segmentRegisterIndex);
    }

    public Dictionary<AddressOperation, List<int>> GetAddressOperations() {
        return _addressOperations;
    }

    public string GetName() {
        return _name;
    }
}