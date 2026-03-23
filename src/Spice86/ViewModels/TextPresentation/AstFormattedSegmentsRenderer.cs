namespace Spice86.ViewModels.TextPresentation;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

/// <summary>
/// AST visitor that produces <see cref="FormattedTextOffset"/> lists with
/// formatter text kind annotations matching the disassembly view.
/// </summary>
public class AstFormattedTextOffsetsRenderer : AstRendererVisitor<List<FormattedTextOffset>> {
    public AstFormattedTextOffsetsRenderer(AsmRenderingConfig config) : base(config, new FormattedTextOffsetsAstOutputRenderer()) {
    }
}
