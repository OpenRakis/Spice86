namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// How one <c>int</c> site of a generated method ends up printed, decided at lowering and refined
/// by the interrupt service call pass.
/// </summary>
internal enum InterruptSiteKind {
    /// <summary>Printed as a typed service helper call (<c>Dos.PrintString(...)</c>).</summary>
    Typed,

    /// <summary>Every observed target is the installed stub, but the selector register has no
    /// known constant value at the site.</summary>
    SelectorUnknown,

    /// <summary>Every observed target is the installed stub and the selector value is known, but
    /// that value has no declared contract or an <see cref="Emulator.InterruptHandlers.Common.Contracts.ServiceContract.Unknown"/>
    /// contract.</summary>
    NoContract,

    /// <summary>Every observed target is the installed stub and the selector value is a known
    /// constant whose entry has a precise contract, but no implementation binding (or a binding
    /// the site's constant selector inputs do not satisfy). The site keeps real interrupt
    /// dispatch, with the precise contract's normal-return effects.</summary>
    BuiltInDispatch,

    /// <summary>Every observed target is the installed stub; the pass has not classified the site
    /// yet (or cannot, inside an opaque statement).</summary>
    BuiltInNotAnalysed,

    /// <summary>At least one observed target is an installed handler but not all of them are the
    /// vector's stub (the program hooked the vector or pointed it elsewhere).</summary>
    Hooked,

    /// <summary>No observed target is an emulator-installed handler: the vector points at program
    /// code (or the setup is unknown).</summary>
    ProgramHandler,

    /// <summary>The vector is not a constant or the <c>int</c> has no observed targets.</summary>
    NotObserved,

    /// <summary>The call returns to an address that is not its expected return; it keeps the
    /// redirected-return dispatch.</summary>
    RedirectedReturn
}
