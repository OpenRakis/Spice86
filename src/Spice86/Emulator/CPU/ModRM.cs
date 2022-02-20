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
    private int _registerMemoryIndex;

    public ModRM(Machine machine, Cpu cpu) {
        _machine = machine;
        _cpu = cpu;
        _memory = machine.Memory;
        _state = cpu.State;
        _staticAddressesRecorder = cpu.StaticAddressesRecorder;
    }

    public uint GetAddress(int defaultSegmentRegisterIndex, ushort offset, bool recordAddress) {
        int? segmentIndex = _state.SegmentOverrideIndex;
        if (segmentIndex == null) {
            segmentIndex = defaultSegmentRegisterIndex;
        }

        if (recordAddress) {
            _staticAddressesRecorder.SetCurrentValue((int)segmentIndex, offset);
        }

        ushort segment = _state.SegmentRegisters.GetRegister((int)segmentIndex);
        return MemoryUtils.ToPhysicalAddress(segment, offset);
    }

    public uint GetAddress(int defaultSegmentRegisterIndex, ushort offset) {
        return GetAddress(defaultSegmentRegisterIndex, offset, false);
    }

    public uint? MemoryAddress { get; private set; }

    public ushort? MemoryOffset { get; private set; }

    public ushort R16 { get => _state.Registers.GetRegister(RegisterIndex); set => _state.Registers.SetRegister(RegisterIndex, value); }

    public byte R8 { get => _state.Registers.GetRegisterFromHighLowIndex8(RegisterIndex); set => _state.Registers.SetRegisterFromHighLowIndex8(RegisterIndex, value); }

    public int RegisterIndex { get; private set; }

    public ushort GetRm16() {
        if (MemoryAddress == null) {
            return _state.Registers.GetRegister(_registerMemoryIndex);
        }

        _staticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.READ, OperandSize.Word16);
        return _memory.GetUint16((uint)MemoryAddress);
    }

    public byte GetRm8() {
        if (MemoryAddress == null) {
            return _state.Registers.GetRegisterFromHighLowIndex8(_registerMemoryIndex);
        }

        _staticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.READ, OperandSize.Byte8);
        return _memory.GetUint8((uint)MemoryAddress);
    }

    public ushort SegmentRegister { get => _state.SegmentRegisters.GetRegister(RegisterIndex); set => _state.SegmentRegisters.SetRegister(RegisterIndex, value); }

    public void Read() {
        byte modRM = _cpu.NextUint8();
        /*
         * bit 7 & bit 6 = mode
         * bit 5 through bit 3 = registerIndex
         * bit 2 through bit 0 = registerMemoryIndex
         */
        int mode = (modRM >> 6) & 0b11;
        RegisterIndex = ((modRM >> 3) & 0b111);
        _registerMemoryIndex = (modRM & 0b111);
        if (mode == 3) {
            // value at reg[memoryRegisterIndex] to be used instead of memoryAddress
            MemoryOffset = null;
            MemoryAddress = null;
            return;
        }
        short disp = 0;
        if (mode == 1) {
            disp = (sbyte)_cpu.NextUint8();
        } else if (mode == 2) {
            disp = (short)_cpu.NextUint16();
        }
        bool bpForRm6 = mode != 0;
        MemoryOffset = (ushort)(ComputeOffset(bpForRm6) + disp);
        MemoryAddress = GetAddress(ComputeDefaultSegment(bpForRm6), (ushort)MemoryOffset, _registerMemoryIndex == 6);
    }

    public void SetRm16(ushort value) {
        if (MemoryAddress == null) {
            _state.Registers.SetRegister(_registerMemoryIndex, value);
        } else {
            _staticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.WRITE, OperandSize.Word16);
            _memory.SetUint16((uint)MemoryAddress, value);
        }
    }

    public void SetRm8(byte value) {
        if (MemoryAddress == null) {
            _state.Registers.SetRegisterFromHighLowIndex8(_registerMemoryIndex, value);
        } else {
            _staticAddressesRecorder.SetCurrentAddressOperation(ValueOperation.WRITE, OperandSize.Byte8);
            _memory.SetUint8((uint)MemoryAddress, value);
        }
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
            0 => (ushort)(_state.BX + _state.SI),
            1 => (ushort)(_state.BX + _state.DI),
            2 => (ushort)(_state.BP + _state.SI),
            3 => (ushort)(_state.BP + _state.DI),
            4 => _state.SI,
            5 => _state.DI,
            6 => bpForRm6 ? _state.BP : _cpu.NextUint16(),
            7 => _state.BX,
            _ => throw new InvalidModeException(_machine, _registerMemoryIndex)
        };
    }
}