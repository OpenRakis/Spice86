namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;

/// <summary>
/// The collector of the constant-offset memory accesses of a method's final IR: every
/// <see cref="SegmentedPointerNode"/> read or write that <see cref="IrGlobalAccesses.TryAccess"/>
/// accepts as a global (segment register other than SS, unsigned byte / word / dword, constant
/// offset). Collected after the pipeline, so a register the constant passes substituted counts as
/// its constant value. Pushes and pops are not <see cref="SegmentedPointerNode"/> accesses and are
/// not collected; absolute pointers and non-register segments are not either.
/// </summary>
internal static class IrConstantMemoryAccesses {
    /// <summary>Collects the accesses of every statement and terminator of every block of
    /// <paramref name="method"/>.</summary>
    public static ConstantMemoryAccessTable Collect(IrMethod method) {
        ConstantMemoryAccessTable table = new();
        foreach (IrBlock block in method.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                Collect(statement.Node, table);
            }
            foreach (IVisitableAstNode root in IrAstChildren.RootsOf(block.Terminator)) {
                Collect(root, table);
            }
        }
        return table;
    }

    private static void Collect(IVisitableAstNode node, ConstantMemoryAccessTable table) {
        ValueNode? storeTarget = node is BinaryOperationNode {
            BinaryOperation: BinaryOperation.ASSIGN,
            Left: SegmentedPointerNode or AbsolutePointerNode } assign ? assign.Left : null;
        // A read-modify-write shares one pointer instance between the store target and the
        // read operand: its first occurrence is the write, every later one a read.
        bool storeTargetCollected = false;
        foreach (IVisitableAstNode descendant in IrAstChildren.Descendants(node)) {
            if (descendant is not SegmentedPointerNode pointer
                || !IrGlobalAccesses.TryAccess(pointer, out ConstantMemoryAccess access)) {
                continue;
            }
            MemoryAccessDirection direction = MemoryAccessDirection.Read;
            if (!storeTargetCollected && ReferenceEquals(descendant, storeTarget)) {
                storeTargetCollected = true;
                direction = MemoryAccessDirection.Write;
            }
            table.Add(access, direction);
        }
    }
}
