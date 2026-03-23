namespace Spice86.ViewModels.TextPresentation;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

/// <summary>
/// AST visitor that produces <see cref="FormattedTextSegment"/> lists with
/// formatter text kind annotations matching the disassembly view.
/// </summary>
public class AstFormattedSegmentsRenderer : AstRendererVisitor<List<FormattedTextSegment>> {
    public AstFormattedSegmentsRenderer(AsmRenderingConfig config) : base(config, new FormattedSegmentsAstOutputRenderer()) {
    }
}
