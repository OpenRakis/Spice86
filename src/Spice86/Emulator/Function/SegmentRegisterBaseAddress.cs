namespace Spice86.Emulator.Function;

using Spice86.Emulator.Memory;

using System.Collections.Generic;

public class SegmentRegisterBasedAddress : SegmentedAddress
{
    private readonly string _name;
    private readonly Dictionary<AddressOperation, List<int>> _addressOperations = new();
    public SegmentRegisterBasedAddress(int segment, int offset, string name) : base(segment, offset)
    {
        this._name = name;
    }

    public virtual string GetName()
    {
        return _name;
    }

    public virtual Dictionary<AddressOperation, List<int>> GetAddressOperations()
    {
        return _addressOperations;
    }

    public virtual void AddAddressOperation(AddressOperation addressOperation, int segmentRegisterIndex)
    {
        if (_addressOperations.TryGetValue(addressOperation, out var segmentRegisterIndexes) == false)
        {
            _addressOperations.Add(addressOperation, new());
        }
        else
        {
            segmentRegisterIndexes.Add(segmentRegisterIndex);
        }
    }
}
