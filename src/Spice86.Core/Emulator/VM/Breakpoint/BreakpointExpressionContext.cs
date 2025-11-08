namespace Spice86.Core.Emulator.VM.Breakpoint;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint.Expression;

/// <summary>
/// Expression context for evaluating breakpoint conditions with access to CPU state and memory.
/// </summary>
public class BreakpointExpressionContext : IExpressionContext {
    private readonly State _state;
    private readonly IMemory _memory;
    private readonly long _triggerAddress;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="BreakpointExpressionContext"/> class.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="memory">The memory interface.</param>
    /// <param name="triggerAddress">The address that triggered the breakpoint evaluation.</param>
    public BreakpointExpressionContext(State state, IMemory memory, long triggerAddress) {
        _state = state;
        _memory = memory;
        _triggerAddress = triggerAddress;
    }
    
    /// <inheritdoc/>
    public long GetVariable(string variableName) {
        return variableName.ToLowerInvariant() switch {
            "address" => _triggerAddress,
            "ax" => _state.AX,
            "bx" => _state.BX,
            "cx" => _state.CX,
            "dx" => _state.DX,
            "si" => _state.SI,
            "di" => _state.DI,
            "sp" => _state.SP,
            "bp" => _state.BP,
            "al" => _state.AL,
            "ah" => _state.AH,
            "bl" => _state.BL,
            "bh" => _state.BH,
            "cl" => _state.CL,
            "ch" => _state.CH,
            "dl" => _state.DL,
            "dh" => _state.DH,
            "cs" => _state.CS,
            "ds" => _state.DS,
            "es" => _state.ES,
            "ss" => _state.SS,
            "ip" => _state.IP,
            "flags" => _state.Flags.FlagRegister,
            "cycles" => _state.Cycles,
            _ => throw new ArgumentException($"Unknown variable: {variableName}")
        };
    }
    
    /// <inheritdoc/>
    public byte ReadMemoryByte(long address) {
        if (address < 0 || address >= _memory.Length) {
            return 0;
        }
        // Use SneakilyRead to avoid triggering memory breakpoints during condition evaluation
        return _memory.SneakilyRead((uint)address);
    }
    
    /// <inheritdoc/>
    public ushort ReadMemoryWord(long address) {
        if (address < 0 || address + 1 >= _memory.Length) {
            return 0;
        }
        // Use SneakilyRead to avoid triggering memory breakpoints
        byte low = _memory.SneakilyRead((uint)address);
        byte high = _memory.SneakilyRead((uint)address + 1);
        return (ushort)((high << 8) | low);
    }
    
    /// <inheritdoc/>
    public uint ReadMemoryDword(long address) {
        if (address < 0 || address + 3 >= _memory.Length) {
            return 0;
        }
        // Use SneakilyRead to avoid triggering memory breakpoints
        ushort low = ReadMemoryWord(address);
        ushort high = ReadMemoryWord(address + 2);
        return (uint)((high << 16) | low);
    }
}
