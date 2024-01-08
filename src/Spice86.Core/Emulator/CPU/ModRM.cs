using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Utils;

public class ModRM {
    private readonly Cpu _cpu;
    private readonly Memory.IMemory _memory;
    private readonly State _state;
    private uint _registerMemoryIndex;

    public ModRM(IMemory memory, Cpu cpu, State state) {
        _cpu = cpu;
        _memory = memory;
        _state = state;
    }

    public uint GetAddress(uint defaultSegmentRegisterIndex, ushort offset) {
        uint segmentIndex = _state.SegmentOverrideIndex??defaultSegmentRegisterIndex;

        ushort segment = _state.SegmentRegisters.UInt16[segmentIndex];
        return MemoryUtils.ToPhysicalAddress(segment, offset);
    }

    public uint? MemoryAddress { get; private set; }

    public ushort? MemoryOffset { get; private set; }

    public uint R32 { get => _state.GeneralRegisters.UInt32[RegisterIndex]; set =>_state.GeneralRegisters.UInt32[RegisterIndex] = value; }

    public ushort R16 { get => _state.GeneralRegisters.UInt16[RegisterIndex]; set =>_state.GeneralRegisters.UInt16[RegisterIndex] = value; }

    public byte R8 { get => _state.GeneralRegisters.UInt8HighLow[RegisterIndex]; set => _state.GeneralRegisters.UInt8HighLow[RegisterIndex] = value; }

    public uint RegisterIndex { get; private set; }

    public uint GetRm32() {
        if (MemoryAddress == null) {
            return _state.GeneralRegisters.UInt32[_registerMemoryIndex];
        }
        return _memory.UInt32[(uint)MemoryAddress];
    }

    public ushort GetRm16() {
        if (MemoryAddress == null) {
            return _state.GeneralRegisters.UInt16[_registerMemoryIndex];
        }
        return _memory.UInt16[(uint)MemoryAddress];
    }

    public byte GetRm8() {
        if (MemoryAddress == null) {
            return _state.GeneralRegisters.UInt8HighLow[_registerMemoryIndex];
        }
        return _memory.UInt8[(uint)MemoryAddress];
    }

    public ushort SegmentRegister { get => _state.SegmentRegisters.UInt16[RegisterIndex]; set => _state.SegmentRegisters.UInt16[RegisterIndex] = value; }

    public void Read() {
        byte modRM = _cpu.NextUint8();
        /*
         * bit 7 & bit 6 = mode
         * bit 5 through bit 3 = registerIndex
         * bit 2 through bit 0 = registerMemoryIndex
         */
        int mode = modRM >> 6 & 0b11;
        RegisterIndex = (uint)(modRM >> 3 & 0b111);
        _registerMemoryIndex = (uint)(modRM & 0b111);
        if (mode == 3) {
            // value at reg[memoryRegisterIndex] to be used instead of memoryAddress
            MemoryOffset = null;
            MemoryAddress = null;
            return;
        }

        if (_cpu.AddressSize == 16) {
            short displacement = mode switch {
                1 => (sbyte)_cpu.NextUint8(),
                2 => (short)_cpu.NextUint16(),
                _ => 0
            };
            ushort offset = ComputeOffset16(mode);
            MemoryOffset = (ushort)(offset + displacement);
        } else {
            long offset = ComputeOffset32(mode, _registerMemoryIndex);
            int displacement = mode switch {
                1 => (sbyte)_cpu.NextUint8(),
                2 => (int)_cpu.NextUint32(),
                _ => 0
            };
            offset += displacement;
            if (offset is > ushort.MaxValue or < ushort.MinValue) {
                throw new CpuGeneralProtectionFaultException("Displacement overflows 16 bits");
            }
            MemoryOffset = (ushort)offset;
        }

        uint segmentRegisterIndex = ComputeDefaultSegment(mode);
        MemoryAddress = GetAddress(segmentRegisterIndex, (ushort)MemoryOffset);
    }

    public void SetRm32(uint value) {
        if (MemoryAddress == null) {
            _state.GeneralRegisters.UInt32[_registerMemoryIndex] = value;
        } else {
            _memory.UInt32[(uint)MemoryAddress] = value;
        }
    }

    public void SetRm16(ushort value) {
        if (MemoryAddress == null) {
            _state.GeneralRegisters.UInt16[_registerMemoryIndex] = value;
        } else {
            _memory.UInt16[(uint)MemoryAddress] = value;
        }
    }

    public void SetRm8(byte value) {
        if (MemoryAddress == null) {
            _state.GeneralRegisters.UInt8HighLow[_registerMemoryIndex] = value;
        } else {
            _memory.UInt8[(uint)MemoryAddress] = value;
        }
    }

    private uint ComputeDefaultSegment(int mode) {
        // The default segment register is SS for the effective addresses containing a
        // BP index, DS for other effective addresses
        return _registerMemoryIndex switch {
            0 => SegmentRegisters.DsIndex,
            1 => SegmentRegisters.DsIndex,
            2 => SegmentRegisters.SsIndex,
            3 => SegmentRegisters.SsIndex,
            4 => SegmentRegisters.DsIndex,
            5 => SegmentRegisters.DsIndex,
            6 => mode == 0 ? SegmentRegisters.DsIndex : SegmentRegisters.SsIndex,
            7 => SegmentRegisters.DsIndex,
            _ => throw new InvalidModeException(_state, _registerMemoryIndex)
        };
    }

    private ushort ComputeOffset16(int mode) {
        return _registerMemoryIndex switch {
            0 => (ushort)(_state.BX + _state.SI),
            1 => (ushort)(_state.BX + _state.DI),
            2 => (ushort)(_state.BP + _state.SI),
            3 => (ushort)(_state.BP + _state.DI),
            4 => _state.SI,
            5 => _state.DI,
            6 => mode == 0 ? _cpu.NextUint16() : _state.BP,
            7 => _state.BX,
            _ => throw new InvalidModeException(_state, _registerMemoryIndex)
        };
    }

    private uint ComputeOffset32(int mode, uint rm) {
        uint result = rm switch {
            0 => _state.EAX,
            1 => _state.ECX,
            2 => _state.EDX,
            3 => _state.EBX,
            4 => CalculateSib(mode),
            5 => _state.EBP,
            6 => _state.ESI,
            7 => _state.EDI,
            _ => throw new ArgumentOutOfRangeException(nameof(rm), rm, "Register memory index must be between 0 and 7 inclusive")
        };
        return result;
    }

    private uint CalculateSib(int mode) {
        byte sib = _cpu.NextUint8();
        int scale = 1 << (sib >> 6 & 0b11);
        int indexRegister = sib >> 3 & 0b111;
        int baseRegister = sib & 0b111;
        int @base = ComputeSibBase(baseRegister, mode);
        int index = ComputeSibIndex(indexRegister);
        return (uint)(@base + scale * index);
    }

    private int ComputeSibIndex(int indexRegister) {
        uint result = indexRegister switch {
            0 => _state.EAX,
            1 => _state.ECX,
            2 => _state.EDX,
            3 => _state.EBX,
            4 => 0,
            5 => _state.EBP,
            6 => _state.ESI,
            7 => _state.EDI,
            _ => throw new ArgumentOutOfRangeException(nameof(indexRegister), indexRegister, "Index register must be between 0 and 7 inclusive")
        };
        return (int)result;
    }

    private int ComputeSibBase(int baseRegister, int mode) {
        uint result = baseRegister switch {
            0 => _state.EAX,
            1 => _state.ECX,
            2 => _state.EDX,
            3 => _state.EBX,
            4 => _state.ESP,
            5 => mode == 0 ? _cpu.NextUint32() : _state.EBP,
            6 => _state.ESI,
            7 => _state.EDI,
            _ => throw new ArgumentOutOfRangeException(nameof(baseRegister), baseRegister, "Base register must be between 0 and 7 inclusive")
        };
        return (int)result;
    }
}
