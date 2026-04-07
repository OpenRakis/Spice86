namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;

public interface StringInstruction {
    public void ExecuteStringOperation(InstructionExecutionHelper helper);
    
    /// <summary>
    /// Whether this String instruction can modify CPU flags or not
    /// </summary>
    public bool ChangesFlags { get; }
    public RepPrefix? RepPrefix { get; }

    /// <summary>
    /// Creates the core operation block for this string instruction (without REP handling)
    /// </summary>
    /// <param name="builder">The AST builder</param>
    /// <returns>The block node containing the core string operation</returns>
    public Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.BlockNode CreateStringOperationBlock(AstBuilder builder);
}