namespace Spice86.DependencyInjection;

using Spice86.Logging;
using Spice86.Shared.Interfaces;
using Pure.DI;

/// <summary>
/// The DI composition root for the UI.
/// </summary>
public partial class Composition {
    private static void Setup() =>
        DI.Setup(nameof(Composition))
            // Infrastructure
            .Bind<ILoggerPropertyBag>().As(Lifetime.Singleton).To<LoggerPropertyBag>()
            .Bind<ILoggerService>().As(Lifetime.Singleton).To<LoggerService>()
            // Composition Root
            .Root<Program>(nameof(Root));
}
