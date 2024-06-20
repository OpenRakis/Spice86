namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Represents the ModRM byte. A lot of x86 instructions use a ModRM byte to specify an operand or further extend the opcode.
/// </summary>
public class ModRM {
    private readonly Cpu _cpu;
    private readonly IMemory _memory;
    private readonly State _state;
    private uint _registerMemoryIndex;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus</param>
    /// <param name="cpu">The emulated CPU</param>
    /// <param name="state">The CPU Registers and Flags</param>
    public ModRM(IMemory memory, Cpu cpu, State state) {
        _cpu = cpu;
        _memory = memory;
        _state = state;
    }

    /// <summary>
    /// Returns the linear address the ModRM byte can point at.
    /// </summary>
    /// <param name="defaultSegmentRegisterIndex">The segment part of the segmented address.</param>
    /// <param name="offset">The offset part of the segmented address.</param>
    /// <returns>The segment:offset computed into a linear address.</returns>
    public uint GetAddress(uint defaultSegmentRegisterIndex, ushort offset) {
        uint segmentIndex = _state.SegmentOverrideIndex??defaultSegmentRegisterIndex;

        ushort segment = _state.SegmentRegisters.UInt16[segmentIndex];
        return MemoryUtils.ToPhysicalAddress(segment, offset);
    }

    /// <summary>
    /// Gets the linear address the ModRM byte can point at. Can be <c>null</c>.
    /// </summary>
    public uint? MemoryAddress { get; private set; }

    /// <summary>
    /// Gets the memory offset of the ModRM byte can point at. Can be <c>null</c>.
    /// </summary>
    public ushort? MemoryOffset { get; private set; }

    /// <summary>
    /// Gets or sets the value of the 32 bit register pointed at by the <see cref="RegisterIndex"/> property.
    /// </summary>
    public uint R32 { get => _state.GeneralRegisters.UInt32[RegisterIndex]; set =>_state.GeneralRegisters.UInt32[RegisterIndex] = value; }

    /// <summary>
    /// Gets or sets the value of the 16 bit register pointed at by the <see cref="RegisterIndex"/> property.
    /// </summary>
    public ushort R16 { get => _state.GeneralRegisters.UInt16[RegisterIndex]; set =>_state.GeneralRegisters.UInt16[RegisterIndex] = value; }

    /// <summary>
    /// Gets or sets the value of the 8 bit register pointed at by the <see cref="RegisterIndex"/> property.
    /// </summary>
    public byte R8 { get => _state.GeneralRegisters.UInt8HighLow[RegisterIndex]; set => _state.GeneralRegisters.UInt8HighLow[RegisterIndex] = value; }

    /// <summary>
    /// Gets the index of the register pointed at by the ModRM byte.
    /// </summary>
    public uint RegisterIndex { get; private set; }

    /// <summary>
    /// If <see cref="MemoryAddress"/> is <c>null</c>, returns the 32 bit value of the register pointed at by the _registerMemoryIndex field. <br/>
    /// If <see cref="MemoryAddress"/> is not <c>null</c>, returns the 32 bit value at the linear address pointed at by the <see cref="MemoryAddress"/> property.
    /// </summary>
    public uint GetRm32() {
        if (MemoryAddress == null) {
            return _state.GeneralRegisters.UInt32[_registerMemoryIndex];
        }
        return _memory.UInt32[(uint)MemoryAddress];
    }

    /// <summary>
    /// If <see cref="MemoryAddress"/> is <c>null</c>, returns the 16 bit value of the register pointed at by the _registerMemoryIndex field. <br/>
    /// If <see cref="MemoryAddress"/> is not <c>null</c>, returns the 16 bit value at the linear address pointed at by the <see cref="MemoryAddress"/> property.
    /// </summary>
    public ushort GetRm16() {
        if (MemoryAddress == null) {
            return _state.GeneralRegisters.UInt16[_registerMemoryIndex];
        }
        return _memory.UInt16[(uint)MemoryAddress];
    }

    /// <summary>
    /// If <see cref="MemoryAddress"/> is <c>null</c>, returns the 8 bit value of the register pointed at by the _registerMemoryIndex field. <br/>
    /// If <see cref="MemoryAddress"/> is not <c>null</c>, returns the 8 bit value at the linear address pointed at by the <see cref="MemoryAddress"/> property.
    /// </summary>
    public byte GetRm8() {
        if (MemoryAddress == null) {
            return _state.GeneralRegisters.UInt8HighLow[_registerMemoryIndex];
        }
        return _memory.UInt8[(uint)MemoryAddress];
    }

    /// <summary>
    /// Gets or sets the value of the segment register pointed at by the <see cref="RegisterIndex"/> property.
    /// </summary>
    public ushort SegmentRegister { get => _state.SegmentRegisters.UInt16[RegisterIndex]; set => _state.SegmentRegisters.UInt16[RegisterIndex] = value; }

    /// <summary>
    /// Parses the ModRM byte of the instruction and sets the <see cref="RegisterIndex"/>, <see cref="MemoryOffset"/> and <see cref="MemoryAddress"/> properties
    /// <para>
    /// Parses the ModR/M byte of the instruction and sets the <see cref="RegisterIndex"/>, <see cref="MemoryOffset"/> and <see cref="MemoryAddress"/> properties.
    /// The ModR/M byte is divided into three parts: <br/>
    /// - The two most significant bits (bit 7 and bit 6) represent the mode. <br/>
    /// - The next three bits (bit 5 through bit 3) represent the register index. <br/>
    /// - The three least significant bits (bit 2 through bit 0) represent the memory register index. <br/>
    /// If the mode is 3, the value at the memory register index is used directly and the memory address is not used. <br/>
    /// If the CPU is in 16-bit addressing mode, a displacement is read based on the mode (8-bit for mode 1, 16-bit for mode 2, and 0 for other modes), and added to the offset computed based on the mode and memory register index. <br/>
    /// If the resulting offset is outside the range of a 16-bit unsigned integer, a general protection fault is thrown. <br/>
    /// The memory address is then computed based on the segment register determined by the mode and the offset. <br/>
    /// </para>
    /// </summary>
    /// <exception cref="CpuGeneralProtectionFaultException">Thrown when the displacement overflows 16 bits.</exception>
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

    /// <summary>
    /// If <see cref="MemoryAddress"/> is <c>null</c>, sets the value of the 32 bit register pointed at by the _registerMemoryIndex field. <br/>
    /// If <see cref="MemoryAddress"/> is not <c>null</c>, sets the 32 bit value at the linear address pointed at by the <see cref="MemoryAddress"/> property.
    /// </summary>
    /// <param name="value">The value to copy.</param>
    public void SetRm32(uint value) {
        if (MemoryAddress == null) {
            _state.GeneralRegisters.UInt32[_registerMemoryIndex] = value;
        } else {
            _memory.UInt32[(uint)MemoryAddress] = value;
        }
    }

    /// <summary>
    /// If <see cref="MemoryAddress"/> is <c>null</c>, sets the value of the 16 bit register pointed at by the _registerMemoryIndex field. <br/>
    /// If <see cref="MemoryAddress"/> is not <c>null</c>, sets the 16 bit value at the linear address pointed at by the <see cref="MemoryAddress"/> property.
    /// </summary>
    /// <param name="value">The value to copy.</param>
    public void SetRm16(ushort value) {
        if (MemoryAddress == null) {
            _state.GeneralRegisters.UInt16[_registerMemoryIndex] = value;
        } else {
            _memory.UInt16[(uint)MemoryAddress] = value;
        }
    }

    /// <summary>
    /// If <see cref="MemoryAddress"/> is <c>null</c>, sets the value of the 8 bit register pointed at by the _registerMemoryIndex field. <br/>
    /// If <see cref="MemoryAddress"/> is not <c>null</c>, sets the 8 bit value at the linear address pointed at by the <see cref="MemoryAddress"/> property.
    /// </summary>
    /// <param name="value">The value to copy.</param>
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
            0 => (uint)SegmentRegisterIndex.DsIndex,
            1 => (uint)SegmentRegisterIndex.DsIndex,
            2 => (uint)SegmentRegisterIndex.SsIndex,
            3 => (uint)SegmentRegisterIndex.SsIndex,
            4 => (uint)SegmentRegisterIndex.DsIndex,
            5 => (uint)SegmentRegisterIndex.DsIndex,
            6 => mode == 0 ? (uint)SegmentRegisterIndex.DsIndex : (uint)SegmentRegisterIndex.SsIndex,
            7 => (uint)SegmentRegisterIndex.DsIndex,
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
