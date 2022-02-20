namespace Spice86.Emulator.Function;

using Spice86.Emulator.Memory;

using System.Collections.Generic;

/// <summary>Represents a memory address, lists how it is accessed.</summary>
public class SegmentRegisterBasedAddress : SegmentedAddress {
    /// <summary>
    ///  Dictionary mapping operation (read / write of 8,16,32 bits length) to segment registers
    /// </summary>
    private readonly Dictionary<AddressOperation, ISet<int>> _addressOperations = new();

    private readonly string? _name;

    public SegmentRegisterBasedAddress(ushort segment, ushort offset, string? name) : base(segment, offset) {
        this._name = name;
    }

    public void AddAddressOperation(AddressOperation addressOperation, int segmentRegisterIndex) {
        if (!_addressOperations.TryGetValue(addressOperation, out ISet<int>? segmentRegisterIndexes)) {
            segmentRegisterIndexes = new HashSet<int>();
            _addressOperations.Add(addressOperation, segmentRegisterIndexes);
        }
        segmentRegisterIndexes.Add(segmentRegisterIndex);
    }

    public Dictionary<AddressOperation, ISet<int>> GetAddressOperations() {
        return _addressOperations;
    }

    public string? Name => _name;
}