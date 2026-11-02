namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

/// <summary>The lowered code of one block, ready to be placed by <see cref="MethodStructurizer"/>.</summary>
/// <typeparam name="TBlock">The block type; used for identity only.</typeparam>
/// <param name="Block">The block.</param>
/// <param name="Entry">The goto target that lands at the start of the block.</param>
/// <param name="HasEntryCheck">Whether the block starts with a <c>CheckExternalEvents</c> call.</param>
/// <param name="HoldsEntry">Whether the block holds a method entry block.</param>
/// <param name="LoopHeaderNeedsCheck">Whether the block needs <see cref="HasEntryCheck"/> to head a natural
/// loop: only when a back edge enters the block from outside one instruction's control flow.</param>
/// <param name="EnteredOnlyFromInside">Whether every edge entering the block stays inside one instruction's
/// control flow: it marks dispatch arms that only the owning instruction's switch reaches, so a lone such
/// arm is still nested in its <c>case</c> section.</param>
/// <param name="Body">The instruction boundaries of the block, in instruction order, each ending with its explicit transfers.</param>
internal sealed record LoweredBlock<TBlock>(TBlock Block, IGotoTarget Entry, bool HasEntryCheck, bool HoldsEntry,
    bool LoopHeaderNeedsCheck, bool EnteredOnlyFromInside, BlockNode Body) where TBlock : class {
    /// <summary>A block that is not entered only from inside.</summary>
    public LoweredBlock(TBlock block, IGotoTarget entry, bool hasEntryCheck, bool holdsEntry,
        bool loopHeaderNeedsCheck, BlockNode body)
        : this(block, entry, hasEntryCheck, holdsEntry, loopHeaderNeedsCheck, false, body) {
    }
}
