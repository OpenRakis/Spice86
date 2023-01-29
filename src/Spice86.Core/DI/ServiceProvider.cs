using Jab;

using Serilog;

using Spice86.Logging;

namespace Spice86.Core.DI;

/// <summary>
/// Source-generated class containing injected deps <br/>
/// Deps are resolved at build time. A service not found will generate a compilation error, not a runtime error. <br/>
/// The generated code can be debugged. <br/>
/// </summary>
[ServiceProvider]
[Singleton(typeof(ILoggerService), typeof(LoggerService))]
public partial class ServiceProvider {
    
}

public static class ServiceProviderExtensions {
    public static ILogger GetLoggerForContext<T>(this ServiceProvider serviceProvider) {
        ILogger logger = serviceProvider.GetService<ILoggerService>().Logger.ForContext<T>();
        return logger;
    }
}