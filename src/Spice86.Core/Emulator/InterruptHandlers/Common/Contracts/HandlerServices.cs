namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// The services an interrupt handler exposes on its vector: either one service covering
/// the whole vector, or a set of services a register selects between.
/// </summary>
public abstract record HandlerServices {
    private HandlerServices() {
    }

    /// <summary>One behavior for the whole vector.</summary>
    /// <param name="Service">The entry of the vector's single service.</param>
    public sealed record SingleService(InterruptServiceEntry Service) : HandlerServices;

    /// <summary>The services the handler's selector register chooses between.</summary>
    /// <param name="Selector">The register the handler reads to select the service.</param>
    /// <param name="Services">The service entries, keyed by selector value.</param>
    public sealed record SelectedServices(ServiceSelector Selector,
        IReadOnlyDictionary<int, InterruptServiceEntry> Services) : HandlerServices;
}
