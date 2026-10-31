namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

/// <summary>
/// The machine values and tokens one statement reads before writing them, always writes and may
/// write, plus whether it can fault and whether it delivers hardware interrupts.
/// <see cref="MayWrite"/> always includes <see cref="MustWrite"/>.
/// </summary>
internal readonly record struct IrEffects {
    /// <summary>Creates an effect; <paramref name="mustWrite"/> is added to <paramref name="mayWrite"/>.</summary>
    public IrEffects(IrValues reads, IrValues mustWrite, IrValues mayWrite, IrTokens tokenReads,
        IrTokens tokenWrites, bool mayFault, bool deliversInterrupts) {
        Reads = reads;
        MustWrite = mustWrite;
        MayWrite = mayWrite | mustWrite;
        TokenReads = tokenReads;
        TokenWrites = tokenWrites;
        MayFault = mayFault;
        DeliversInterrupts = deliversInterrupts;
    }

    /// <summary>Values whose value before the statement can change what it does.</summary>
    public IrValues Reads { get; }

    /// <summary>Values written on every path.</summary>
    public IrValues MustWrite { get; }

    /// <summary>Values written on at least one path; contains <see cref="MustWrite"/>.</summary>
    public IrValues MayWrite { get; }

    /// <summary>Tokens whose version before the statement is read.</summary>
    public IrTokens TokenReads { get; }

    /// <summary>Tokens the statement writes.</summary>
    public IrTokens TokenWrites { get; }

    /// <summary>Whether the statement's instruction has an observed CPU fault edge.</summary>
    public bool MayFault { get; }

    /// <summary>Whether the statement can deliver hardware interrupts.</summary>
    public bool DeliversInterrupts { get; }

    /// <summary>No effect.</summary>
    public static IrEffects None { get; } =
        new(IrValues.None, IrValues.None, IrValues.None, IrTokens.None, IrTokens.None, false, false);

    /// <summary>The conservative effect of code the analysis does not know: reads all values, may
    /// write all values, reads and writes both tokens.</summary>
    public static IrEffects Unknown { get; } =
        new(IrValues.All, IrValues.None, IrValues.All, IrTokens.All, IrTokens.All, false, false);

    /// <summary>The effect of a transfer out of the method: it uses every value and both tokens.</summary>
    public static IrEffects ExitUse { get; } =
        new(IrValues.All, IrValues.None, IrValues.None, IrTokens.All, IrTokens.None, false, false);

    /// <summary>This effect followed by <paramref name="next"/>.</summary>
    public IrEffects Then(IrEffects next) =>
        new(Reads | (next.Reads & ~MustWrite), MustWrite | next.MustWrite, MayWrite | next.MayWrite,
            TokenReads | next.TokenReads, TokenWrites | next.TokenWrites,
            MayFault || next.MayFault, DeliversInterrupts || next.DeliversInterrupts);

    /// <summary>Exactly one of two effects, not known which.</summary>
    public static IrEffects Either(IrEffects first, IrEffects second) =>
        new(first.Reads | second.Reads, first.MustWrite & second.MustWrite, first.MayWrite | second.MayWrite,
            first.TokenReads | second.TokenReads, first.TokenWrites | second.TokenWrites,
            first.MayFault || second.MayFault, first.DeliversInterrupts || second.DeliversInterrupts);

    /// <summary>Whether the effects read or write memory.</summary>
    public bool TouchesMemory => ((TokenReads | TokenWrites) & IrTokens.Memory) != IrTokens.None;
}
