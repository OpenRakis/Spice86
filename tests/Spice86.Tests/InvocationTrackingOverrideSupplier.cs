namespace Spice86.Tests;

using Microsoft.Extensions.Logging;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Test-only wrapper around a supplied <see cref="IOverrideSupplier"/> that records which registered
/// overrides the emulated program actually entered. It proves the compiled generated code ran instead of
/// being merely installed: a supplier whose overrides are never invoked is detected even when the
/// interpreter alone would have produced the right result. Emulator-provided device and DOS callbacks are
/// unaffected; only the delegates of the wrapped supplier are observed.
/// </summary>
internal sealed class InvocationTrackingOverrideSupplier : IOverrideSupplier {
    private readonly IOverrideSupplier _inner;
    private readonly HashSet<SegmentedAddress> _invokedAddresses = new();

    /// <summary>Creates the wrapper.</summary>
    /// <param name="inner">Supplier whose overrides are tracked.</param>
    public InvocationTrackingOverrideSupplier(IOverrideSupplier inner) {
        _inner = inner;
    }

    /// <summary>Addresses of the wrapped supplier's overrides that were entered at least once.</summary>
    public IReadOnlySet<SegmentedAddress> InvokedAddresses => _invokedAddresses;

    /// <inheritdoc />
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        ILogger loggerService, Configuration configuration, ushort programStartAddress, Machine machine) {
        IDictionary<SegmentedAddress, FunctionInformation> functions =
            _inner.GenerateFunctionInformations(loggerService, configuration, programStartAddress, machine);
        Dictionary<SegmentedAddress, FunctionInformation> tracked = new();
        foreach ((SegmentedAddress address, FunctionInformation functionInformation) in functions) {
            Func<int, Action>? functionOverride = functionInformation.FunctionOverride;
            tracked[address] = functionOverride == null
                ? functionInformation
                : new FunctionInformation(address, functionInformation.Name, Wrap(address, functionOverride)) {
                    ForbidsInterpreterFallback = functionInformation.ForbidsInterpreterFallback
                };
        }
        return tracked;
    }

    private Func<int, Action> Wrap(SegmentedAddress address, Func<int, Action> functionOverride) {
        return loadOffset => {
            _invokedAddresses.Add(address);
            return functionOverride(loadOffset);
        };
    }
}
