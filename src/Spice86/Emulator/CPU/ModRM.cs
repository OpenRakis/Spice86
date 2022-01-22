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
    private uint? _memoryAddress;
    private ushort? _memoryOffset;
    private int _registerIndex;
    private int _registerMemoryIndex;

    public ModRM(Machine machine, Cpu cpu) {
        _machine = machine;
        _cpu = cpu;
        _memory = machine.GetMemory();
        _state = cpu.GetState();
        _staticAddressesRecorder = cpu.GetStaticAddressesRecorder();
    }

    public uint GetAddress(int defaultSegmentRegisterIndex, ushort offset, bool recordAddress) {
        int? segmentIndex = _state.GetSegmentOverrideIndex();
        if (segmentIndex == null) {
            segmentIndex = defaultSegmentRegisterIndex;
        }

        if (recordAddress) {
            _staticAddressesRecorder.SetCurrentValue((int)segmentIndex, offset);
        }

        ushort segment = _state.GetSegmentRegisters().GetRegister((int)segmentIndex);
        return MemoryUtils.ToPhysicalAddress(segment, offset);
    }

    public uint GetAddress(int defaultSegmentRegisterIndex, ushort offset) {
        return GetAddress(defaultSegmentRegisterIndex, offset, false);
    }

    public uint? GetMemoryAddress() {
        return _memoryAddress;
    }

    public ushort? GetMemoryOffset() {
        return _memoryOffset;
    }

    public ushort GetR16() {
        return _state.GetRegisters().GetRegister(_registerIndex);
    }

    public byte GetR8() {
        return _state.GetRegisters().GetRegisterFromHighLowIndex8(_registerIndex);
    }

    public int GetRegisterIndex() {
        return _registerIndex;
    }

    public ushort GetRm16() {
        if (_memoryAddress == null) {
            return _state.GetRegisters().GetRegister(_registerMemoryIndex);
        }

        _staticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.READ, OperandSize.Word16);
        return _memory.GetUint16((uint)_memoryAddress);
    }

    public byte GetRm8() {
        if (_memoryAddress == null) {
            return _state.GetRegisters().GetRegisterFromHighLowIndex8(_registerMemoryIndex);
        }

        _staticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.READ, OperandSize.Byte8);
        return _memory.GetUint8((uint)_memoryAddress);
    }

    public int GetSegmentRegister() {
        return _state.GetSegmentRegisters().GetRegister(_registerIndex);
    }

    public void Read() {
        byte modRM = _cpu.NextUint8();
        /*
         * bit 7 & bit 6 = mode
         * bit 5 through bit 3 = registerIndex
         * bit 2 through bit 0 = registerMemoryIndex
         */
        int mode = (modRM >> 6) & 0b11;
        _registerIndex = ((modRM >> 3) & 0b111);
        _registerMemoryIndex = (modRM & 0b111);
        if (mode == 3) {
            // value at reg[memoryRegisterIndex] to be used instead of memoryAddress
            _memoryOffset = null;
            _memoryAddress = null;
            return;
        }
        short disp = 0;
        if (mode == 1) {
            disp = (sbyte)_cpu.NextUint8();
        } else if (mode == 2) {
            disp = (short)_cpu.NextUint16();
        }
        bool bpForRm6 = mode != 0;
        _memoryOffset = (ushort)(ComputeOffset(bpForRm6) + disp);
        _memoryAddress = GetAddress(ComputeDefaultSegment(bpForRm6), (ushort)_memoryOffset, _registerMemoryIndex == 6);
    }

    public void SetR16(ushort value) {
        _state.GetRegisters().SetRegister(_registerIndex, value);
    }

    public void SetR8(byte value) {
        _state.GetRegisters().SetRegisterFromHighLowIndex8(_registerIndex, value);
    }

    public void SetRm16(ushort value) {
        if (_memoryAddress == null) {
            _state.GetRegisters().SetRegister(_registerMemoryIndex, value);
        } else {
            _staticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.WRITE, OperandSize.Word16);
            _memory.SetUint16((uint)_memoryAddress, value);
        }
    }

    public void SetRm8(byte value) {
        if (_memoryAddress == null) {
            _state.GetRegisters().SetRegisterFromHighLowIndex8(_registerMemoryIndex, value);
        } else {
            _staticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.WRITE, OperandSize.Byte8);
            _memory.SetUint8((uint)_memoryAddress, value);
        }
    }

    public void SetSegmentRegister(ushort value) {
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
            _ => throw new InvalidModeException(_machine, _registerMemoryIndex)
        };
    }

    private ushort ComputeOffset(bool bpForRm6) {
        return _registerMemoryIndex switch {
            0 => (ushort)(_state.GetBX() + _state.GetSI()),
            1 => (ushort)(_state.GetBX() + _state.GetDI()),
            2 => (ushort)(_state.GetBP() + _state.GetSI()),
            3 => (ushort)(_state.GetBP() + _state.GetDI()),
            4 => _state.GetSI(),
            5 => _state.GetDI(),
            6 => bpForRm6 ? _state.GetBP() : _cpu.NextUint16(),
            7 => _state.GetBX(),
            _ => throw new InvalidModeException(_machine, _registerMemoryIndex)
        };
    }
}