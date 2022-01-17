namespace Spice86.Emulator.CPU;

using Spice86.Emulator.Function;
using Spice86.Emulator.VM;
using Spice86.Emulator.Memory;

public class ModRM {
    private readonly Cpu _cpu;
    private readonly Machine _machine;
    private readonly Memory _memory;
    private readonly State _state;
    private readonly StaticAddressesRecorder _staticAddressesRecorder;
    private int? _memoryAddress;
    private int? _memoryOffset;
    private int _registerIndex;
    private uint _registerMemoryIndex;

    public ModRM(Machine machine, Cpu cpu) {
        _machine = machine;
        _cpu = cpu;
        _memory = machine.GetMemory();
        _state = cpu.GetState();
        _staticAddressesRecorder = cpu.GetStaticAddressesRecorder();
    }

    public int GetAddress(int defaultSegmentRegisterIndex, int offset, bool recordAddress) {
        int? segmentIndex = _state.GetSegmentOverrideIndex();
        if (segmentIndex == null) {
            segmentIndex = defaultSegmentRegisterIndex;
        }

        if (recordAddress) {
            _staticAddressesRecorder.SetCurrentValue((int)segmentIndex, offset);
        }

        int segment = _state.GetSegmentRegisters().GetRegister((int)segmentIndex);
        return MemoryUtils.ToPhysicalAddress(segment, offset);
    }

    public int GetAddress(int defaultSegmentRegisterIndex, int offset) {
        return GetAddress(defaultSegmentRegisterIndex, offset, false);
    }

    public int? GetMemoryAddress() {
        return _memoryAddress;
    }

    public int? GetMemoryOffset() {
        return _memoryOffset;
    }

    public int GetR16() {
        return _state.GetRegisters().GetRegister(_registerIndex);
    }

    public int GetR8() {
        return _state.GetRegisters().GetRegisterFromHighLowIndex8(_registerIndex);
    }

    public int GetRegisterIndex() {
        return _registerIndex;
    }

    public int GetRm16() {
        if (_memoryAddress == null) {
            return _state.GetRegisters().GetRegister((int)_registerMemoryIndex);
        }

        _staticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.READ, OperandSize.Word16);
        return _memory.GetUint16((int)_memoryAddress);
    }

    public int GetRm8() {
        if (_memoryAddress == null) {
            return _state.GetRegisters().GetRegisterFromHighLowIndex8((int)_registerMemoryIndex);
        }

        _staticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.READ, OperandSize.Byte8);
        return _memory.GetUint8((int)_memoryAddress);
    }

    public int GetSegmentRegister() {
        return _state.GetSegmentRegisters().GetRegister(_registerIndex);
    }

    public void Read() {
        uint modRM = _cpu.NextUint8();
        /**
         * bit 7 & bit 6 = mode bit 5 through bit 3 = registerIndex bit 2 through bit 0 = registerMemoryIndex
         */
        uint mode = (modRM >> 6) & 0b11;
        _registerIndex = (int)((modRM >> 3) & 0b111);
        _registerMemoryIndex = modRM & 0b111;
        if (mode == 3) {
            // value at reg[memoryRegisterIndex] to be used instead of memoryAddress
            _memoryOffset = null;
            _memoryAddress = null;
            return;
        }
        var disp = 0;
        if (mode == 1) {
            disp = _cpu.NextUint8();
        } else if (mode == 2) {
            disp = _cpu.NextUint16();
        }
        bool bpForRm6 = mode != 0;
        _memoryOffset = ComputeOffset(bpForRm6) + disp;
        _memoryAddress = GetAddress((int)ComputeDefaultSegment(bpForRm6), (int)_memoryOffset, _registerMemoryIndex == 6);
    }

    public void SetR16(int value) {
        _state.GetRegisters().SetRegister(_registerIndex, value);
    }

    public void SetR8(int value) {
        _state.GetRegisters().SetRegisterFromHighLowIndex8(_registerIndex, value);
    }

    public void SetRm16(int value) {
        if (_memoryAddress == null) {
            _state.GetRegisters().SetRegister((int)_registerMemoryIndex, value);
        } else {
            _staticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.WRITE, OperandSize.Word16);
            _memory.SetUint16((int)_memoryAddress, (ushort)value);
        }
    }

    public void SetRm8(int value) {
        if (_memoryAddress == null) {
            _state.GetRegisters().SetRegisterFromHighLowIndex8((int)_registerMemoryIndex, value);
        } else {
            _staticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.WRITE, OperandSize.Byte8);
            _memory.SetUint8((int)_memoryAddress, (byte)value);
        }
    }

    public void SetSegmentRegister(int value) {
        _state.GetSegmentRegisters().SetRegister(_registerIndex, value);
    }

    private int ComputeDefaultSegment(bool bpForRm6) {
        // The default segment register is SS for the effective addresses containing a
        // BP index, DS for other effective addresses
        return _registerMemoryIndex switch {
            0 => SegmentRegisters.DsIndex,
            1 => SegmentRegisters.DsIndex,
            2 => SegmentRegisters.SsIndex,
            3 => SegmentRegisters.SsIndex,
            4 => SegmentRegisters.DsIndex,
            5 => SegmentRegisters.DsIndex,
            6 => bpForRm6 ? SegmentRegisters.SsIndex : SegmentRegisters.DsIndex,
            7 => SegmentRegisters.DsIndex,
            _ => throw new InvalidModeException(_machine, (int)_registerMemoryIndex)
        };
    }

    private int ComputeOffset(bool bpForRm6) {
        return _registerMemoryIndex switch {
            0 => _state.GetBX() + _state.GetSI(),
            1 => _state.GetBX() + _state.GetDI(),
            2 => _state.GetBP() + _state.GetSI(),
            3 => _state.GetBP() + _state.GetDI(),
            4 => _state.GetSI(),
            5 => _state.GetDI(),
            6 => bpForRm6 ? _state.GetBP() : _cpu.NextUint16(),
            7 => _state.GetBX(),
            _ => throw new InvalidModeException(_machine, (int)_registerMemoryIndex)
        };
    }
}