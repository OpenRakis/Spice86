namespace Spice86.Emulator.Function;

using Spice86.Emulator.Memory;

using System.Collections.Generic;

/// <summary>Represents a memory address, lists how it is accessed.</summary>
public class SegmentRegisterBasedAddress : SegmentedAddress {
    /// <summary>
    ///  Dictionary mapping operation (read / write of 8,16,32 bits length) to segment registers
    /// </summary>
    private readonly Dictionary<AddressOperation, List<int>> _addressOperations = new();

    private readonly string _name;

    public SegmentRegisterBasedAddress(ushort segment, ushort offset, string name) : base(segment, offset) {
        this._name = name;
    }

    public void AddAddressOperation(AddressOperation addressOperation, int segmentRegisterIndex) {
        List<int> segmentRegisterIndexes = _addressOperations.ComputeIfAbsent(addressOperation, new());
        segmentRegisterIndexes.Add(segmentRegisterIndex);
    }

    public Dictionary<AddressOperation, List<int>> GetAddressOperations() {
        return _addressOperations;
    }

    public string GetName() {
        return _name;
    }
}