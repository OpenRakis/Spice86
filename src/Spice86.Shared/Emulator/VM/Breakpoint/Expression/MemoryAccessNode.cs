namespace Spice86.Shared.Emulator.VM.Breakpoint.Expression;

/// <summary>
/// Memory access size for reading from memory.
/// </summary>
public enum MemoryAccessSize {
    /// <summary>Byte (8-bit) access</summary>
    Byte,
    /// <summary>Word (16-bit) access</summary>
    Word,
    /// <summary>Dword (32-bit) access</summary>
    Dword
}

/// <summary>
/// Represents a memory access operation in an expression.
/// </summary>
public class MemoryAccessNode : IExpressionNode {
    /// <summary>
    /// The address expression.
    /// </summary>
    public IExpressionNode Address { get; }
    
    /// <summary>
    /// The size of the memory access.
    /// </summary>
    public MemoryAccessSize Size { get; }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryAccessNode"/> class.
    /// </summary>
    /// <param name="address">The address expression.</param>
    /// <param name="size">The size of the memory access.</param>
    public MemoryAccessNode(IExpressionNode address, MemoryAccessSize size) {
        Address = address;
        Size = size;
    }
    
    /// <inheritdoc/>
    public long Evaluate(IExpressionContext context) {
        long addr = Address.Evaluate(context);
        
        return Size switch {
            MemoryAccessSize.Byte => context.ReadMemoryByte(addr),
            MemoryAccessSize.Word => context.ReadMemoryWord(addr),
            MemoryAccessSize.Dword => context.ReadMemoryDword(addr),
            _ => throw new InvalidOperationException($"Unknown memory access size: {Size}")
        };
    }
    
    /// <inheritdoc/>
    public override string ToString() {
        string sizeStr = Size switch {
            MemoryAccessSize.Byte => "byte",
            MemoryAccessSize.Word => "word",
            MemoryAccessSize.Dword => "dword",
            _ => "?"
        };
        return $"{sizeStr}[{Address}]";
    }
}
