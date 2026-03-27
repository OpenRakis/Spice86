namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

public class AstInstructionRenderer : AstRendererVisitor<string> {
    public AstInstructionRenderer(AsmRenderingConfig config) : base(config, new StringAstOutputRenderer()) {
    }
}