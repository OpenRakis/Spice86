namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

/// <summary>The status flags an AST reads before writing them, always writes, and may write.</summary>
internal readonly record struct FlagEffect {
    /// <summary>Creates an effect; <paramref name="mustWrite"/> is added to <paramref name="mayWrite"/>.</summary>
    public FlagEffect(StatusFlags reads, StatusFlags mustWrite, StatusFlags mayWrite) {
        Reads = reads;
        MustWrite = mustWrite;
        MayWrite = mayWrite | mustWrite;
    }

    /// <summary>Flags whose value before the AST can change what it does.</summary>
    public StatusFlags Reads { get; }

    /// <summary>Flags written on every path.</summary>
    public StatusFlags MustWrite { get; }

    /// <summary>Flags written on at least one path; contains <see cref="MustWrite"/>.</summary>
    public StatusFlags MayWrite { get; }

    /// <summary>No flag read or written.</summary>
    public static FlagEffect None { get; } = new(StatusFlags.None, StatusFlags.None, StatusFlags.None);

    /// <summary>The conservative effect of code the analysis does not know: reads all flags, may write all flags.</summary>
    public static FlagEffect Unknown { get; } = new(StatusFlags.All, StatusFlags.None, StatusFlags.All);

    /// <summary>Reads <paramref name="flags"/>.</summary>
    public static FlagEffect Reading(StatusFlags flags) => new(flags, StatusFlags.None, StatusFlags.None);

    /// <summary>Always writes <paramref name="flags"/>.</summary>
    public static FlagEffect Writing(StatusFlags flags) => new(StatusFlags.None, flags, flags);

    /// <summary>This effect followed by <paramref name="next"/>.</summary>
    public FlagEffect Then(FlagEffect next) =>
        new(Reads | (next.Reads & ~MustWrite), MustWrite | next.MustWrite, MayWrite | next.MayWrite);

    /// <summary>Exactly one of two effects, not known which.</summary>
    public static FlagEffect Either(FlagEffect first, FlagEffect second) =>
        new(first.Reads | second.Reads, first.MustWrite & second.MustWrite, first.MayWrite | second.MayWrite);
}
