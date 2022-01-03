namespace Spice86.Emulator.Function;

using Spice86.Emulator.Cpu;
using Spice86.Emulator.Memory;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class StaticAddressesRecorder
{
    private readonly bool _debugMode;
    private readonly SegmentRegisters _segmentRegisters;
    private readonly HashSet<SegmentedAddress> _whiteListOfSegmentForOffset = new();
    private readonly Dictionary<int, SegmentRegisterBasedAddress> _segmentRegisterBasedAddress = new();
    private readonly Dictionary<int, string> _names = new();
    private int? _currentSegmentIndex;
    private int? _currentOffset;
    private AddressOperation? _currentAddressOperation;
    public StaticAddressesRecorder(State state, bool debugMode)
    {
        this._debugMode = debugMode;
        this._segmentRegisters = state.GetSegmentRegisters();
    }

    public virtual Dictionary<int, String> GetNames()
    {
        return _names;
    }

    public virtual void AddName(int physicalAddress, string name)
    {
        _names.Add(physicalAddress, name);
    }

    public virtual void Reset()
    {
        _currentSegmentIndex = null;
        _currentOffset = null;
        _currentAddressOperation = null;
    }

    public virtual void Commit()
    {
        if (_debugMode && _currentSegmentIndex != null && _currentOffset != null && _currentAddressOperation != null && _currentSegmentIndex != null)
        {
            int segmentValue = _segmentRegisters.GetRegister(_currentSegmentIndex.Value);
            int physicalAddress = MemoryUtils.ToPhysicalAddress(segmentValue, _currentOffset.Value);
            if (_segmentRegisterBasedAddress.TryGetValue(physicalAddress, out var value) == false)
            {
                value = new SegmentRegisterBasedAddress(segmentValue, _currentOffset.Value, _names[physicalAddress]);
                _segmentRegisterBasedAddress.Add(physicalAddress, value);
            }
            value.AddAddressOperation(_currentAddressOperation, _currentSegmentIndex.Value);
        }
    }

    public virtual void SetCurrentValue(int regIndex, int offset)
    {
        _currentSegmentIndex = regIndex;
        _currentOffset = offset;
    }

    public virtual void SetCurrentAddressOperation(ValueOperation valueOperation, OperandSize operandSize)
    {
        _currentAddressOperation = new AddressOperation(valueOperation, operandSize);
    }

    public virtual ICollection<SegmentRegisterBasedAddress> GetSegmentRegisterBasedAddress()
    {
        return _segmentRegisterBasedAddress.Values;
    }

    public virtual HashSet<SegmentedAddress> GetWhiteListOfSegmentForOffset()
    {
        return _whiteListOfSegmentForOffset;
    }

    public virtual void AddSegmentTowhiteList(SegmentedAddress address)
    {
        _whiteListOfSegmentForOffset.Add(address);
    }
}
