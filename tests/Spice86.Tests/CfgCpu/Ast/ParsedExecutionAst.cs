namespace Spice86.Tests.CfgCpu.Ast;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Parses one instruction from raw bytes with the real parser and returns its execution AST.
/// </summary>
internal static class ParsedExecutionAst {
    public static IVisitableAstNode Of(params byte[] bytes) {
        Memory memory = new(new AddressReadWriteBreakpoints(), new Ram(0x100000), new A20Gate(), new RealModeMmu8086(), false);
        State state = new(CpuModel.INTEL_80386);
        InstructionParser parser = new(memory, state, new SequentialIdAllocator());
        for (int i = 0; i < bytes.Length; i++) {
            memory.UInt8[i] = bytes[i];
        }
        CfgInstruction instruction = parser.ParseInstructionAt(new SegmentedAddress(0, 0));
        return instruction.ExecutionAst;
    }
}
