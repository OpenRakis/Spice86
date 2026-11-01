namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// The services an interrupt handler exposes on its vector: either one contract covering
/// the whole vector, or a set of contracts a register selects between.
/// </summary>
public abstract record HandlerServices {
    private HandlerServices() {
    }

    /// <summary>One behavior for the whole vector.</summary>
    /// <param name="Contract">The contract of the vector's single service.</param>
    public sealed record SingleService(ServiceContract Contract) : HandlerServices;

    /// <summary>The services the handler's selector register chooses between.</summary>
    /// <param name="Selector">The register the handler reads to select the service.</param>
    /// <param name="Contracts">The service contracts, keyed by selector value.</param>
    public sealed record SelectedServices(ServiceSelector Selector,
        IReadOnlyDictionary<int, ServiceContract> Contracts) : HandlerServices;
}
