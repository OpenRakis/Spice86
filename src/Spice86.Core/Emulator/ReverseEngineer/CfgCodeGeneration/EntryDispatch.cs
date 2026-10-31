namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// The entry dispatch of a generated method: a <c>switch (loadOffset)</c> jumping to each secondary
/// entry, empty for a single-entry method. <paramref name="targetOf"/> gives the goto target an entry
/// jumps to.
/// </summary>
internal static class EntryDispatch {
    /// <summary>The entry dispatch of <paramref name="method"/>.</summary>
    /// <exception cref="InvalidOperationException">The primary entry of a single-entry method is not the first emitted node.</exception>
    public static BlockNode Build(CfgGeneratorContext context, MethodPlan method,
        Func<ICfgNode, IGotoTarget> targetOf) {
        if (method.NeedsEntryDispatch) {
            List<SwitchSection> sections = [];
            foreach (CfgCodePartitionEntry entry in method.Entries) {
                List<IVisitableAstNode> entryBody = [];
                if (!entry.Node.Equals(entry.Block.Entry)) {
                    entryBody.Add(new CheckExternalEventsNode(entry.Address));
                }
                entryBody.Add(new GotoNode(targetOf(entry.Node)));
                sections.Add(Section((uint)context.GetEntryLoadOffset(method.Partition, entry.Node), Sequence(entryBody)));
            }
            return Sequence(new SwitchNode(new LoadOffsetParameterNode(), sections, Sequence(UntestedMessages.EntryOffset())));
        }

        // Single entry: the block layout starts with the primary entry block, so no jump is needed.
        ICfgNode primaryEntry = method.PrimaryEntry.Node;
        if (!method.NodeEmissionPlans[0].Node.Equals(primaryEntry)) {
            throw new InvalidOperationException($"Primary entry {primaryEntry.Address} of {method.MethodName} is not the first emitted node.");
        }
        return Empty();
    }
}
