namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;

using System.Linq;

/// <summary>
/// The flag read census classification: attributes one <see cref="FlagSetterKind"/>,
/// one <see cref="FlagSetterDistance"/> and, for single setters, one <see cref="FlagPropagationBlocker"/>
/// to every status-flag read the structured body prints, using the method's IR SSA. Reasons come from
/// the SSA, the statement ASTs and the block layout, never from the printed text. The statement nodes
/// are the objects the structured body shares, so every printed <see cref="CpuFlagNode"/> maps back to
/// the statement or terminator position it was lifted into.
/// </summary>
internal static class IrFlagReadCensusClassifier {
    /// <summary>Classifies every status-flag read of <paramref name="body"/> through <paramref name="ssa"/>.</summary>
    public static FlagReadCensus Classify(IrMethod method, IrSsa ssa, IVisitableAstNode body) {
        FlagReadCensus census = new();
        Positions(method, ssa, out Dictionary<IVisitableAstNode, (IrBlock Block, int Index)> positions,
            out Dictionary<int, (IrStatement Statement, int Index)> setterByVersion);
        Classifier classifier = new(ssa, positions, setterByVersion);
        classifier.Walk(body, null, false, census);
        return census;
    }

    /// <summary>Every statement and terminator AST node to the position it was lifted into, by reference,
    /// and every version to the statement currently defining it, so a read looks up its setter in
    /// constant time instead of scanning the block.</summary>
    private static void Positions(IrMethod method, IrSsa ssa,
        out Dictionary<IVisitableAstNode, (IrBlock Block, int Index)> positions,
        out Dictionary<int, (IrStatement Statement, int Index)> setterByVersion) {
        positions = new(ReferenceEqualityComparer.Instance);
        setterByVersion = [];
        foreach (IrBlock block in method.Blocks) {
            for (int index = 0; index < block.Statements.Count; index++) {
                IrStatement statement = block.Statements[index];
                foreach (IVisitableAstNode node in IrAstChildren.Descendants(statement.Node)) {
                    positions[node] = (block, index);
                }
                foreach ((IrSsaValue _, int version) in ssa.Definitions(statement)) {
                    setterByVersion.TryAdd(version, (statement, index));
                }
            }
            foreach (IVisitableAstNode node in IrAstChildren.RootsOf(block.Terminator)) {
                positions[node] = (block, block.Statements.Count);
            }
        }
    }

    private sealed class Classifier(
        IrSsa ssa,
        Dictionary<IVisitableAstNode, (IrBlock Block, int Index)> positions,
        Dictionary<int, (IrStatement Statement, int Index)> setterByVersion) {

        /// <summary>Walks the structured body on an explicit stack, so deeply nested <c>if</c> / <c>else</c>
        /// chains cannot overflow the thread's stack; <paramref name="position"/> is the lifted position of
        /// the nearest enclosing statement or terminator node and <paramref name="inWrite"/> whether
        /// <paramref name="node"/> is the target of an assignment (a flag write, not a read).</summary>
        public void Walk(IVisitableAstNode node, (IrBlock Block, int Index)? position, bool inWrite,
            FlagReadCensus census) {
            Stack<(IVisitableAstNode Node, (IrBlock Block, int Index)? Position, bool InWrite)> pending = new();
            pending.Push((node, position, inWrite));
            while (pending.Count > 0) {
                (IVisitableAstNode current, (IrBlock Block, int Index)? currentPosition, bool currentInWrite) =
                    pending.Pop();
                if (positions.TryGetValue(current, out (IrBlock Block, int Index) found)) {
                    currentPosition = found;
                }
                if (!currentInWrite && currentPosition is { } read && current is CpuFlagNode flag
                    && ValueOf(flag.FlagMask) is IrSsaValue value) {
                    ClassifyRead(read, value, census);
                }
                List<(IVisitableAstNode, bool)> children = [.. ChildrenOf(current)];
                for (int index = children.Count - 1; index >= 0; index--) {
                    pending.Push((children[index].Item1, currentPosition, children[index].Item2));
                }
            }
        }

        private static IEnumerable<(IVisitableAstNode, bool)> ChildrenOf(IVisitableAstNode node) {
            if (node is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assign) {
                yield return (assign.Left, true);
                yield return (assign.Right, false);
                yield break;
            }
            foreach (IVisitableAstNode child in IrAstChildren.ChildrenOf(node)) {
                yield return (child, false);
            }
        }

        private void ClassifyRead((IrBlock Block, int Index) read, IrSsaValue value,
            FlagReadCensus census) {
            int version = ssa.VersionBefore(read.Block, read.Index, value);
            IrSsaVersionInfo info = ssa.Info(version);
            (IrStatement? setter, int setterIndex) = SetterAt(version);
            FlagSetterKind kind = info.Kind switch {
                IrSsaDefinitionKind.MethodEntry => FlagSetterKind.EntrySetter,
                IrSsaDefinitionKind.Phi => FlagSetterKind.JoinSetter,
                _ => SetterKindOf(setter)
            };
            FlagSetterDistance distance = DistanceOf(kind, info, setter, setterIndex, read);
            FlagPropagationBlocker? blocker = distance == FlagSetterDistance.NoSingleSetter
                ? null
                : BlockerOf(info, setter, setterIndex, read);
            census.Add(kind, distance, blocker);
        }

        /// <summary>The statement <paramref name="version"/> was defined at and its current index,
        /// from the site map built after the passes so edits cannot stale it.</summary>
        private (IrStatement?, int) SetterAt(int version) =>
            setterByVersion.TryGetValue(version, out (IrStatement Statement, int Index) site)
                ? site
                : (null, -1);

        private static FlagSetterKind SetterKindOf(IrStatement? setter) {
            if (setter is null) {
                return FlagSetterKind.PlainSetter;
            }
            if (IrAstChildren.Descendants(setter.Node).Any(IsAluCall)) {
                return FlagSetterKind.AluSetter;
            }
            if (IrAstChildren.Descendants(setter.Node).Any(IsFlagRegisterAssignment)) {
                return FlagSetterKind.FlagRegisterSetter;
            }
            return FlagSetterKind.PlainSetter;
        }

        private static FlagSetterDistance DistanceOf(FlagSetterKind kind, IrSsaVersionInfo info,
            IrStatement? setter, int setterIndex, (IrBlock Block, int Index) read) {
            if (kind is FlagSetterKind.JoinSetter or FlagSetterKind.EntrySetter
                or FlagSetterKind.BoundarySetter
                || setter is null) {
                return FlagSetterDistance.NoSingleSetter;
            }
            if (!ReferenceEquals(info.Block, read.Block)) {
                return FlagSetterDistance.OtherBlockSetter;
            }
            IrStatement? reader = read.Index < read.Block.Statements.Count
                ? read.Block.Statements[read.Index]
                : null;
            if (setterIndex == read.Index - 1
                || (reader is not null && setter.Origin.Equals(reader.Origin))) {
                return FlagSetterDistance.AdjacentSetter;
            }
            return FlagSetterDistance.SameBlockSetter;
        }

        private FlagPropagationBlocker BlockerOf(IrSsaVersionInfo info, IrStatement? setter,
            int setterIndex, (IrBlock Block, int Index) read) {
            if (info.Kind == IrSsaDefinitionKind.PartialDefinition) {
                return FlagPropagationBlocker.MayWrite;
            }
            if (setter is not null) {
                if (IrAstChildren.Descendants(setter.Node)
                    .Any(node => node is SegmentedPointerNode or AbsolutePointerNode)) {
                    return FlagPropagationBlocker.MemoryOperand;
                }
                for (IrSsaValue register = IrSsaValue.Eax; register < IrSsaValue.Carry; register++) {
                    if ((setter.Effects.Reads & IrSsaValues.MaskOf(register)) != IrValues.None
                        && ssa.VersionBefore(read.Block, read.Index, register)
                            != ssa.VersionAfter(info.Block, setterIndex, register)) {
                        return FlagPropagationBlocker.OperandChanged;
                    }
                }
            }
            return FlagPropagationBlocker.Propagatable;
        }

        private static IrSsaValue? ValueOf(ushort mask) => mask switch {
            Flags.Carry => IrSsaValue.Carry,
            Flags.Parity => IrSsaValue.Parity,
            Flags.Auxiliary => IrSsaValue.Auxiliary,
            Flags.Zero => IrSsaValue.Zero,
            Flags.Sign => IrSsaValue.Sign,
            Flags.Overflow => IrSsaValue.Overflow,
            _ => null
        };

        private static bool IsAluCall(IVisitableAstNode node) =>
            node is AluOperationNode or AluStatementNode;

        private static bool IsFlagRegisterAssignment(IVisitableAstNode node) =>
            node is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Left: FlagRegisterNode };
    }
}
