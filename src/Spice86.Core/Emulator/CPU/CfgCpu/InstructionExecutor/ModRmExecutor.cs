namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.Memory;

public class ModRmExecutor {
    private readonly State _state;
    private readonly IMemory _memory;
    private readonly ModRmComputer _computer;

    public ModRmExecutor(State state, IMemory memory, InstructionFieldValueRetriever instructionFieldValueRetriever) {
        _state = state;
        _memory = memory;
        _computer = new(state, instructionFieldValueRetriever);
    }

    private ModRmContext ModRmContext => _computer.ModRmContext;

    /// <summary>
    /// Call this before executing instructions. This computes a new value for the memory offset and makes the class use the new ModRmContext.
    /// </summary>
    public void RefreshWithNewModRmContext(ModRmContext modRmContext) {
        _computer.ModRmContext = modRmContext;
        MemoryOffset = _computer.ComputeMemoryOffset();
        MemoryAddress = _computer.ComputeMemoryAddress(MemoryOffset);
    }

    /// <summary>
    /// Gets the linear address the ModRM byte can point at. Can be <c>null</c>.
    /// </summary>
    public uint? MemoryAddress  { get; private set; }
    
    /// <summary>
    /// Gets the MemoryAddress field, crashes if it was <c>null</c>.
    /// </summary>
    public uint MandatoryMemoryAddress {
        get {
            if (MemoryAddress == null) {
                throw new MemoryAddressMandatoryException(_state);
            }

            return MemoryAddress.Value;
        }
    }
    
    /// <summary>
    /// Gets the memory offset of the ModRM byte can point at. Can be <c>null</c>.
    /// </summary>
    public ushort? MemoryOffset { get; private set; }

    /// <summary>
    /// Gets the MemoryOffset field, crashes if it was <c>null</c>.
    /// </summary>
    public ushort MandatoryMemoryOffset {
        get {
            if (MemoryOffset == null) {
                throw new MemoryAddressMandatoryException(_state);
            }

            return MemoryOffset.Value;
        }
    }

    /// <summary>
    /// Computes a physical address from an offset and the segment register used in this modrm operation
    /// </summary>
    /// <param name="offset"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public uint GetPhysicalAddress(ushort offset) {
        return _computer.GetPhysicalAddress(offset);
    }

    /// <summary>
    /// Gets or sets the value of the 32 bit register pointed at by the <see cref="RegisterIndex"/> property.
    /// </summary>
    public uint R32 { get => _state.GeneralRegisters.UInt32[RegisterIndex]; set => _state.GeneralRegisters.UInt32[RegisterIndex] = value; }

    /// <summary>
    /// Gets or sets the value of the 16 bit register pointed at by the <see cref="RegisterIndex"/> property.
    /// </summary>
    public ushort R16 { get => _state.GeneralRegisters.UInt16[RegisterIndex]; set => _state.GeneralRegisters.UInt16[RegisterIndex] = value; }

    /// <summary>
    /// Gets or sets the value of the 8 bit register pointed at by the <see cref="RegisterIndex"/> property.
    /// </summary>
    public byte R8 { get => _state.GeneralRegisters.UInt8HighLow[RegisterIndex]; set => _state.GeneralRegisters.UInt8HighLow[RegisterIndex] = value; }

    public uint RM32 {
        get {
            if (MemoryAddress == null) {
                return _state.GeneralRegisters.UInt32[ModRmContext.RegisterMemoryIndex];
            }
            return _memory.UInt32[(uint)MemoryAddress];
        }
        set {
            if (MemoryAddress == null) {
                _state.GeneralRegisters.UInt32[ModRmContext.RegisterMemoryIndex] = value;
            } else {
                _memory.UInt32[(uint)MemoryAddress] = value;
            }
        }
    }

    public ushort RM16 {
        get {
            if (MemoryAddress == null) {
                return _state.GeneralRegisters.UInt16[ModRmContext.RegisterMemoryIndex];
            }
            return _memory.UInt16[(uint)MemoryAddress];
        }
        set {
            if (MemoryAddress == null) {
                _state.GeneralRegisters.UInt16[ModRmContext.RegisterMemoryIndex] = value;
            } else {
                _memory.UInt16[(uint)MemoryAddress] = value;
            }
        }
    }

    public byte RM8 {
        get {
            if (MemoryAddress == null) {
                return _state.GeneralRegisters.UInt8HighLow[ModRmContext.RegisterMemoryIndex];
            }
            return _memory.UInt8[(uint)MemoryAddress];
        }
        set {
            if (MemoryAddress == null) {
                _state.GeneralRegisters.UInt8HighLow[ModRmContext.RegisterMemoryIndex] = value;
            } else {
                _memory.UInt8[(uint)MemoryAddress] = value;
            }
        }
    }

    /// <summary>
    /// Gets the index of the register pointed at by the ModRM byte.
    /// </summary>
    public int RegisterIndex => ModRmContext.RegisterIndex;
}