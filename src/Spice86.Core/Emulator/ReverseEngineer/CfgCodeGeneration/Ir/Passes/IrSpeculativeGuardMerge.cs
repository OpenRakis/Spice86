namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Merges the per-instruction speculative guards of a straight-line speculative run into one
/// <see cref="SpeculativeGuardNode"/> holding every signature field of the run, printed as a single
/// <c>VerifySpeculativeRunOrFail</c> at the run's first instruction. A run is only merged when all of
/// these hold, stated with the IR effects model:
/// <list type="bullet">
/// <item><description>SG-1: no statement of the run writes memory that may alias the code bytes -
/// a <see cref="IrTokens.Memory"/> write that is not <see cref="IrMemoryAliasAnalysis.IsFullMemoryBoundary"/>
/// keeps the merge only when every access of <see cref="IrMemoryAliasAnalysis.Writes"/> is
/// <see cref="IrMemoryAliasAnalysis.AllowsRam"/> and <see cref="IrAliasRelation.Disjoint"/> from the
/// raw linear range run start .. block code end. The code range needs no
/// <see cref="IrMemoryAliasAnalysis.AllowsRam"/> proof of its own: a write proven to be conventional
/// RAM below 0xA0000 cannot reach a device mapping, nothing at or above 0xA0000 is backed by
/// conventional RAM, and the A20 wrap is already handled by <see cref="IrMemoryAliasAnalysis.Relation"/>;
/// so a known-RAM write that is linearly disjoint from the code bytes cannot change them, wherever the
/// code is.</description></item>
/// <item><description>SG-2: no statement of the run delivers interrupts
/// (<see cref="IrEffects.DeliversInterrupts"/>) - an interrupt delivered mid-run can write the
/// run's bytes.</description></item>
/// <item><description>SG-3: no statement of the run may fault (<see cref="IrEffects.MayFault"/>) -
/// a fault handler can leave the run or change its bytes.</description></item>
/// <item><description>SG-4: no statement of the run accesses a port
/// (<see cref="IrEffects.TokenWrites"/> &amp; <see cref="IrTokens.Io"/>) - a device the port access
/// arms can write the run's bytes through DMA.</description></item>
/// <item><description>SG-5: the guarded instructions are contiguous in memory - a later guard joins the
/// open run only when its offset is the end offset of the run's last instruction, so no observed
/// instruction and no other byte sits between two merged guards.</description></item>
/// </list>
/// SG-2 to SG-4 have no ASM fixture: a trap for them would need a hardware interrupt, a fault or a DMA
/// write patching a later speculative instruction at a chosen moment, so they are covered by unit
/// tests only (SG-1 by the existing self-modification fixture). Runs never cross a block boundary: a
/// jump target in the middle of a run starts a new block, whose first statement is a boundary, not a
/// body statement of the previous run.
/// </summary>
internal static class IrSpeculativeGuardMerge {
    /// <summary>Merges the speculative guards of every block of <paramref name="method"/>.
    /// Runs on the final statements: it must run last so no other pass sees a merged guard.</summary>
    /// <summary>Applies the pass in strict mode.</summary>
    public static void Apply(IrMethod method, IrEditor editor) =>
        Apply(method, editor, GenerationPolicy.Strict);

    public static void Apply(IrMethod method, IrEditor editor, GenerationPolicy policy) {
        List<(IrBlock Block, int Index, IrStatement Statement)> replacements = [];
        List<(IrBlock Block, int Index)> removals = [];
        foreach (IrBlock block in method.Blocks) {
            CollectBlock(block, new IrMemoryAliasAnalysis(method, editor.Ssa, policy), replacements, removals);
        }
        foreach ((IrBlock block, int index, IrStatement merged) in replacements) {
            editor.Replace(block, index, merged);
        }
        foreach ((IrBlock block, int index) in removals.OrderByDescending(pair => pair.Index)) {
            editor.Remove(block, index);
        }
    }

    private sealed class GuardRun {
        public required int Index { get; init; }
        public required CfgInstruction FirstInstruction { get; init; }
        public required List<string> Fields { get; init; }
        public required int RunEnd { get; set; }
    }

    private static void CollectBlock(IrBlock block, IrMemoryAliasAnalysis alias,
        List<(IrBlock Block, int Index, IrStatement Statement)> replacements,
        List<(IrBlock Block, int Index)> removals) {
        Dictionary<ushort, int> blockCodeEndBySegment = BlockCodeEndBySegment(block);
        GuardRun? run = null;
        for (int index = 0; index < block.Statements.Count; index++) {
            IrStatement statement = block.Statements[index];
            if (statement.Node is SpeculativeGuardNode guard && statement.Origin is CfgInstruction instruction) {
                if (run is not null
                    && guard.Address.Segment == run.FirstInstruction.Address.Segment
                    && guard.Address.Offset == run.RunEnd) {
                    run.Fields.AddRange(guard.SignatureFields);
                    run.RunEnd = instruction.Address.Offset + instruction.Length;
                    removals.Add((block, index));
                    continue;
                }
                Close(block, run, replacements);
                run = new GuardRun {
                    Index = index,
                    FirstInstruction = instruction,
                    Fields = [.. guard.SignatureFields],
                    RunEnd = instruction.Address.Offset + instruction.Length
                };
                continue;
            }
            if (run is not null && IsBarrier(block, index, alias, run, blockCodeEndBySegment)) {
                Close(block, run, replacements);
                run = null;
            }
        }
        Close(block, run, replacements);
    }

    private static void Close(IrBlock block, GuardRun? run,
        List<(IrBlock Block, int Index, IrStatement Statement)> replacements) {
        if (run is null || run.Fields.Count == 1) {
            return;
        }
        replacements.Add((block, run.Index, IrStatement.Create(
            new SpeculativeGuardNode(run.FirstInstruction.Address, run.Fields),
            run.FirstInstruction)));
    }

    /// <summary>The largest <c>offset + Length</c> of the block's <see cref="CfgInstruction"/>
    /// origins, per segment: the code byte range a mid-run write must be proved disjoint from.</summary>
    private static Dictionary<ushort, int> BlockCodeEndBySegment(IrBlock block) {
        Dictionary<ushort, int> ends = new();
        foreach (CfgInstruction instruction in block.Statements.Select(s => s.Origin).OfType<CfgInstruction>()) {
            int end = instruction.Address.Offset + instruction.Length;
            if (!ends.TryGetValue(instruction.Address.Segment, out int current) || end > current) {
                ends[instruction.Address.Segment] = end;
            }
        }
        return ends;
    }

    /// <summary>Whether <paramref name="statement"/> can change the open run's code bytes or leave the
    /// run before a later guard re-checks them: the barrier conditions SG-1 to SG-4.</summary>
    private static bool IsBarrier(IrBlock block, int index, IrMemoryAliasAnalysis alias,
        GuardRun run, Dictionary<ushort, int> blockCodeEndBySegment) {
        IrStatement statement = block.Statements[index];
        IrEffects effects = statement.Effects;
        if (effects.DeliversInterrupts || effects.MayFault
            || (effects.TokenWrites & IrTokens.Io) != IrTokens.None) {
            return true;
        }
        if ((effects.TokenWrites & IrTokens.Memory) == IrTokens.None) {
            return false;
        }
        return !MemoryWriteProvedDisjoint(block, index, alias, run, blockCodeEndBySegment);
    }

    private static bool MemoryWriteProvedDisjoint(IrBlock block, int index,
        IrMemoryAliasAnalysis alias, GuardRun run, Dictionary<ushort, int> blockCodeEndBySegment) {
        if (alias.IsFullMemoryBoundary(block, index)) {
            return false;
        }
        ushort segment = run.FirstInstruction.Address.Segment;
        int blockCodeEnd = blockCodeEndBySegment[segment];
        if (blockCodeEnd > 0x10000) {
            return false;
        }
        int runStartOffset = run.FirstInstruction.Address.Offset;
        IrMemoryAccess codeRange = new() {
            LinearAddress = (uint)(segment * 16 + runStartOffset),
            Width = blockCodeEnd - runStartOffset
        };
        foreach (IrMemoryAccess write in alias.Writes(block, index)) {
            if (!alias.AllowsRam(write) || alias.Relation(write, codeRange) != IrAliasRelation.Disjoint) {
                return false;
            }
        }
        return true;
    }
}
