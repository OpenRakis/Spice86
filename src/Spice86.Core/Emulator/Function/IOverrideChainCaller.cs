namespace Spice86.Core.Emulator.Function;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// The code waiting for an override chain started by one call: either the interpreter, which executed
/// the call instruction, or C# code, which called <c>NearCall</c>, <c>FarCall</c> or <c>InterruptCall</c>.
/// </summary>
internal interface IOverrideChainCaller {
    /// <summary>Address the call expects to return to, or null when the call defines none.</summary>
    SegmentedAddress? ExpectedReturnAddress { get; }

    /// <summary>True when the caller itself continues at the expected return address, so an override
    /// registered there must not run: C# code would otherwise execute that code twice. False for the
    /// interpreter, which only dispatches overrides on call instructions and would otherwise execute the
    /// original assembly at that address.</summary>
    bool ContinuesAtExpectedReturn { get; }

    /// <summary>Tells whether control landing on <paramref name="landedAddress"/> is an accepted
    /// redirected return of the call, so the chain ends there without running any override.
    /// The interpreter accepts none; generated C# code accepts the return addresses the call was
    /// observed to be redirected to, with the stack back at or above its pre-call level.</summary>
    /// <param name="landedAddress">The address control landed on.</param>
    /// <param name="stackPhysicalAddress">The physical stack address at that moment.</param>
    /// <returns>True when the chain must end at <paramref name="landedAddress"/>.</returns>
    bool AcceptsRedirectedReturn(SegmentedAddress landedAddress, uint stackPhysicalAddress);

    /// <summary>Runs one override reached by the chain.</summary>
    /// <param name="function">The override's registration.</param>
    /// <param name="functionOverride">The override to run.</param>
    /// <returns>True when the call ended inside the override, so the chain must stop.</returns>
    bool RunOverride(FunctionInformation function, Func<int, Action> functionOverride);

    /// <summary>Called when the machine stopped during the chain, before the chain ends.</summary>
    void OnMachineStopped();
}
