namespace Spice86.ViewModels.TextPresentation;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

/// <summary>
/// AST visitor that produces <see cref="FormattedTextToken"/> lists with
/// formatter text kind annotations matching the disassembly view.
/// </summary>
public class AstFormattedTextTokensRenderer : AstRendererVisitor<List<FormattedTextToken>> {
    public AstFormattedTextTokensRenderer(AsmRenderingConfig config) : base(config, new FormattedTextTokensAstOutputRenderer()) {
    }
}
