namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory.Indexable;

/// <summary>
/// Shared toolkit passed to all instruction parsers. Holds the reader, ModRM parser,
/// CPU state, prefix parser, and AST builder that every parser needs.
/// </summary>
public class ParsingTools {
    public InstructionReader InstructionReader { get; }
    public InstructionPrefixParser InstructionPrefixParser { get; }
    public ModRmParser ModRmParser { get; }
    public State State { get; }
    public AstBuilder AstBuilder { get; }

    public ParsingTools(IIndexable memory, State state) {
        InstructionReader instructionReader = new(memory);
        InstructionReader = instructionReader;
        InstructionPrefixParser = new(instructionReader);
        ModRmParser = new(instructionReader, state);
        State = state;
        AstBuilder = new AstBuilder();
    }
}
