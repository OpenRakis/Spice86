namespace Spice86.Tests.CfgCpu;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Centralizes instruction creation and assertion for tests migrating away from concrete instruction types.
/// </summary>
public class TestInstructionHelper {
    private readonly Memory _memory;
    private readonly State _state;
    private readonly InstructionParser _parser;
    private readonly AstInstructionRenderer _renderer;

    public TestInstructionHelper() {
        _memory = new Memory(new(), new Ram(0x100000), new A20Gate(), new RealModeMmu386(), false);
        _state = new State(CpuModel.INTEL_80286);
        _parser = new InstructionParser(_memory, _state);
        _renderer = new AstInstructionRenderer(AsmRenderingConfig.CreateSpice86Style());
    }

    /// <summary>
    /// The memory instance used by this helper.
    /// </summary>
    public Memory Memory => _memory;

    /// <summary>
    /// The CPU state instance used by this helper.
    /// </summary>
    public State State => _state;

    /// <summary>
    /// Writes instructions to memory using MemoryAsmWriter, then parses the result.
    /// </summary>
    public CfgInstruction WriteAndParse(SegmentedAddress address, Action<MemoryAsmWriter> write) {
        MemoryAsmWriter writer = new(_memory, address);
        write(writer);
        return _parser.ParseInstructionAt(address);
    }

    /// <summary>
    /// Renders the display AST to a string for assertion.
    /// </summary>
    public string RenderDisplayAst(CfgInstruction instruction) {
        return instruction.DisplayAst.Accept(_renderer);
    }
}
