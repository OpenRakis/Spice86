namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

/// <summary>
/// What <see cref="CfgCSharpGenerator"/> does besides generating the default source. With every option at its
/// default the generated source is the one the golden files hold.
/// </summary>
public sealed record GeneratorOptions {
    /// <summary>The options of the production generator: every option at its default.</summary>
    public static GeneratorOptions Default { get; } = new();

    /// <summary>The options of a check build, generated code made to be compared with the emulated original:
    /// every instrumentation option on.</summary>
    public static GeneratorOptions CheckBuild { get; } = new() {
        ServiceCallNotifications = true,
        KeepStoreWidths = true
    };

    /// <summary>Whether each emitted method runs the census classifiers the generated-program diagnostics
    /// read. Production skips them.</summary>
    public bool CollectCensuses { get; init; }

    /// <summary>Whether the IR invariants are checked after the passes. Tests enable it.</summary>
    public bool Verify { get; init; }

    /// <summary>Whether a <c>ServiceCalled(vector, selector, inputs)</c> statement is printed before each
    /// direct call of an interrupt service implementation, so the call raises the same
    /// <see cref="Spice86.Core.Emulator.InterruptHandlers.Common.Contracts.IServiceCallObserver"/> event as a
    /// call through the handler's stub. Off by default: the statement costs a call per service call, and the
    /// default output has none.</summary>
    public bool ServiceCallNotifications { get; init; }

    /// <summary>Whether each memory store keeps the width of the instruction it comes from. The joined-memory
    /// pass then leaves two adjacent word stores as they are, where it otherwise stores both words as one
    /// dword at the second store. Off by default: a check build turns it on so that an
    /// <see cref="Spice86.Core.Emulator.Memory.IMemoryWriteObserver"/> sees the same writes, with the same
    /// widths and in the same order, from the generated code as from the emulated original.</summary>
    public bool KeepStoreWidths { get; init; }
}
