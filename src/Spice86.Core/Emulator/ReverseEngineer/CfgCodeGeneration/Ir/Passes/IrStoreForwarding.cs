namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// Forwards the stored value of a store into a later read of the same block when the read provably
/// accesses the same bytes: <c>mov [si],ax</c> followed by <c>mov bx,[si]</c> emits
/// <c>UInt16[DS, SI] = AX; BX = AX;</c>. The store stays; no store is removed.
/// Legality rows, all through <see cref="IrMemoryAliasAnalysis"/> and <see cref="IrSsa.SameVersions"/>:
/// SF-1: <see cref="IrAliasRelation.Equal"/> between the store target and the read, the same
/// <see cref="DataType"/>, both known RAM (<see cref="IrMemoryAliasAnalysis.IsKnownRam"/>);
/// SF-2: the stored value is pure - registers, constants, conversions, unary and non-assignment
/// binary operations, no token read, no call, no temporary;
/// SF-3: no statement strictly between is a <see cref="IrMemoryAliasAnalysis.IsFullMemoryBoundary"/>
/// or <see cref="IrEffects.MayFault"/>, and every write of a statement between is
/// <see cref="IrAliasRelation.Disjoint"/> from the store, known RAM;
/// SF-4: <see cref="IrSsa.SameVersions"/> of the stored value's reads between the store and the
/// reader;
/// SF-5: the reader's origin carries no <see cref="IrInstructionShape"/> tag.
/// </summary>
internal static class IrStoreForwarding {
    /// <summary>Forwards provably equal stores into their readers, block by block.</summary>
    public static void Apply(IrMethod method, IrSsa ssa) {
        IrMemoryAliasAnalysis alias = new(ssa);
        foreach (IrBlock block in method.Blocks) {
            ForwardBlock(block, method, ssa, alias);
        }
    }

    private static void ForwardBlock(IrBlock block, IrMethod method, IrSsa ssa,
        IrMemoryAliasAnalysis alias) {
        for (int s = 0; s < block.Statements.Count; s++) {
            if (block.Statements[s].Node is not BinaryOperationNode {
                    BinaryOperation: BinaryOperation.ASSIGN,
                    Left: ValueNode target, Right: ValueNode stored }
                || target is not (SegmentedPointerNode or AbsolutePointerNode)
                || !Pure(stored)
                || alias.Writes(block, s) is not [IrMemoryAccess storeAccess]
                || !alias.IsKnownRam(storeAccess)) {
                continue;
            }
            IrEffects storedEffects = IrEffectsVisitor.Of(stored, false);
            for (int l = s + 1; l < block.Statements.Count; l++) {
                if (method.Shapes.GetValueOrDefault(block.Statements[l].Origin)
                        != IrInstructionShape.None) {
                    continue;
                }
                while (TryForward(block, s, l, target, storeAccess, storedEffects, ssa, alias)) {
                    // Rescan until no more reads of the stored value fold in.
                }
            }
        }
    }

    /// <summary>Forwards the store at <paramref name="s"/> into the first readable pointer of the
    /// statement at <paramref name="l"/> it is proved equal to; returns whether a read was
    /// replaced so the caller can rescan the rewritten statement.</summary>
    private static bool TryForward(IrBlock block, int s, int l, ValueNode target,
        IrMemoryAccess storeAccess, IrEffects storedEffects, IrSsa ssa,
        IrMemoryAliasAnalysis alias) {
        IrStatement reader = block.Statements[l];
        foreach (ValueNode pointer in ReadPointersOf(reader)) {
            IrMemoryAccess read = alias.OfPointer(pointer, reader);
            IrAliasRelation relation = alias.Relation(storeAccess, read);
            bool ram = alias.IsKnownRam(read);
            bool clear = ClearBetween(block, s, l, storeAccess, alias);
            bool same = ssa.SameVersions(storedEffects.Reads, IrTokens.None,
                (block, s + 1), (block, l));
            if (relation != IrAliasRelation.Equal
                || !ram
                || pointer.DataType != target.DataType
                || !clear
                || !same) {
                continue;
            }
            ValueNode stored = ((BinaryOperationNode)block.Statements[s].Node).Right;
            IVisitableAstNode candidate = ReplaceNode(reader.Node, pointer, stored);
            if (candidate is BinaryOperationNode {
                    BinaryOperation: BinaryOperation.ASSIGN,
                    Left: RegisterNode destination, Right: RegisterNode source }
                && destination.Equals(source)) {
                continue;
            }
            IrStatement replacement = IrStatement.Create(candidate, reader.Origin);
            ssa.Replace(reader, replacement);
            block.Statements[l] = replacement;
            return true;
        }
        return false;
    }

    /// <summary>Whether every statement strictly between <paramref name="s"/> and
    /// <paramref name="l"/> lets the read cross it: no full memory boundary, no fault, and every
    /// memory write is disjoint known RAM from the forwarded store.</summary>
    private static bool ClearBetween(IrBlock block, int s, int l, IrMemoryAccess storeAccess,
        IrMemoryAliasAnalysis alias) {
        for (int k = s + 1; k < l; k++) {
            if (block.Statements[k].Effects.MayFault
                || alias.IsFullMemoryBoundary(block, k)) {
                return false;
            }
            foreach (IrMemoryAccess write in alias.Writes(block, k)) {
                if (!alias.IsKnownRam(write)
                    || alias.Relation(write, storeAccess) != IrAliasRelation.Disjoint) {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>The pointer nodes <paramref name="statement"/> reads: every pointer occurrence
    /// that is not the store target of a plain store.</summary>
    private static IEnumerable<ValueNode> ReadPointersOf(IrStatement statement) {
        ValueNode? storeTarget = statement.Node is BinaryOperationNode {
            BinaryOperation: BinaryOperation.ASSIGN,
            Left: SegmentedPointerNode or AbsolutePointerNode
        } assign ? assign.Left : null;
        return IrAstChildren.Descendants(statement.Node).OfType<ValueNode>()
            .Where(node => node is SegmentedPointerNode or AbsolutePointerNode
                && !ReferenceEquals(node, storeTarget));
    }

    /// <summary>Whether <paramref name="node"/> is a value that can be duplicated into another
    /// statement: registers, constants, conversions, unary and non-assignment binary operations
    /// only - no token read, no call, no temporary.</summary>
    private static bool Pure(ValueNode node) =>
        IrEffectsVisitor.Of(node, false).TokenReads == IrTokens.None
        && IrAstChildren.Descendants(node).All(descendant => descendant switch {
            RegisterNode or ConstantNode or InstructionFieldNode or TypeConversionNode
                or UnaryOperationNode or ConditionalValueNode => true,
            ExplicitCastNode cast => cast.Value is ValueNode,
            BinaryOperationNode binary => binary.BinaryOperation != BinaryOperation.ASSIGN,
            _ => false
        });

    private static IVisitableAstNode ReplaceNode(IVisitableAstNode node, IVisitableAstNode target,
        ValueNode replacement) {
        if (ReferenceEquals(node, target)) {
            return replacement;
        }
        return IrAstChildren.MapChildren(node,
            child => ReplaceNode(child, target, replacement));
    }
}
