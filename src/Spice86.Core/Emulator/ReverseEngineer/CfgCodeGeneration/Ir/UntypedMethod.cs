namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

/// <summary>The first rule of <see cref="TypedSignatures"/> that refused a candidate method a typed
/// signature, so its generated body stays untyped.</summary>
internal enum UntypedReason {
    /// <summary>The method's generation plan has several entry blocks.</summary>
    SeveralEntries,
    /// <summary>The calling conventions do not prove the entry returns.</summary>
    NoProvedReturn,
    /// <summary>An exit keeps an interrupt-return, a cyclic or unproved transfer: the detail holds
    /// the refusing node kind.</summary>
    UntypedExit,
    /// <summary>The body reaches an interrupt service call, a callback or a machine <c>int</c>: the
    /// detail holds the refusing node kind.</summary>
    InterruptOrCallback,
    /// <summary>Neither the inputs nor the outputs hold a register.</summary>
    NoInputsOrOutputs,
    /// <summary>The entry's proved return effect is missing, everything, or differs from the
    /// observed outputs.</summary>
    ReturnEffectDiffers,
    /// <summary>A tail call target stayed untyped, so the method and its unconfirmed targets lose
    /// their signatures: the detail holds the unconfirmed entry.</summary>
    TailCallTargetUntyped
}

/// <summary>One method the typed-signature analysis refused, the first rule that refused it and an
/// optional detail (a node kind or an entry address).</summary>
internal sealed record UntypedMethod(string MethodName, UntypedReason Reason, string? Detail);
