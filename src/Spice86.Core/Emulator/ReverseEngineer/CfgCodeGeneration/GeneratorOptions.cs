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
        BranchProbes = true,
        DeviceOperations = false
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

    /// <summary>Whether the condition of each <c>if</c>, loop and conditional expression of a method body is
    /// printed as <c>Branch(site, condition)</c> and the value of each <c>switch</c> as
    /// <c>Selector(site, value)</c>. Both helpers report to the
    /// <see cref="Spice86.Core.Emulator.ReverseEngineer.IBranchProbeObserver"/> of the machine and return
    /// their argument. A condition joined with <c>&amp;&amp;</c> or <c>||</c> gets one probe per operand, and an
    /// operand that prints as the constant <c>true</c> or <c>false</c> gets none. The entry dispatch of a method with several entries gets none. Off by default.</summary>
    public bool BranchProbes { get; init; }

    /// <summary>Whether a sequence of port accesses that forms one device operation is printed as one call
    /// of the device. On by default. When off, every port access prints on its own, so a tool that
    /// intercepts ports sees the same accesses from generated code as from the emulated original.</summary>
    public bool DeviceOperations { get; init; } = true;

    /// <summary>Whether every instruction keeps its <c>// SSSS:OOOO asm</c> comment; the address map
    /// records the address either way.</summary>
    public bool PrintAsmComments { get; init; }

    /// <summary>The generation policy: the strict generation plus the relaxations it enables.</summary>
    internal GenerationPolicy Policy { get; init; } = GenerationPolicy.Strict;
}
