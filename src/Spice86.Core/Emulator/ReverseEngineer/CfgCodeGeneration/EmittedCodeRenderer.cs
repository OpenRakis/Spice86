namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

/// <summary>
/// Writes statement items to the source writer. This is the dumb printer at the end of the pipeline: it knows
/// nothing about control flow or semantics — it just formats lines, braces, indentation, and switch cases
/// from the already-decided <see cref="StatementItem"/> tree.
/// </summary>
internal static class EmittedCodeRenderer {
    public static void Render(EmittedCode code, CSharpSourceWriter writer) {
        foreach (StatementItem item in code.AsStatements()) {
            Render(item, writer);
        }
    }

    private static void Render(IReadOnlyList<StatementItem> items, CSharpSourceWriter writer) {
        foreach (StatementItem item in items) {
            Render(item, writer);
        }
    }

    private static void Render(StatementItem item, CSharpSourceWriter writer) {
        switch (item) {
            case LineStatement line:
                writer.Line(line.Text);
                return;
            case BlockStatement block:
                writer.OpenBlock(block.Header);
                Render(block.Body, writer);
                writer.CloseBlock();
                return;
            case SwitchStatement switchStatement:
                writer.OpenBlock(switchStatement.Header);
                foreach (SwitchCase switchCase in switchStatement.Cases) {
                    writer.Line($"case {switchCase.Label}:");
                    writer.Indent();
                    Render(switchCase.Body, writer);
                    // A case body that diverges (ends in goto/return/throw) already transfers control, so a
                    // trailing break would be unreachable (CS0162). Only break when control can fall through.
                    if (EmittedCode.SequenceCompletesNormally(switchCase.Body)) {
                        writer.Line("break;");
                    }
                    writer.Dedent();
                }
                writer.Line("default:");
                writer.Indent();
                Render(switchStatement.Default, writer);
                writer.Dedent();
                writer.CloseBlock();
                return;
            default:
                throw new NotSupportedException($"Unsupported {nameof(StatementItem)} shape {item.GetType().Name}.");
        }
    }
}
