namespace Spice86.DependencyInjection;

using Spice86.Core.CLI;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

using StrongInject;

/// <summary>
/// Provides top-level services: Startup, Logging, Configuration.
/// </summary>
[Register(typeof(LoggerPropertyBag), Scope.SingleInstance, typeof(ILoggerPropertyBag))]
[Register(typeof(LoggerService), Scope.SingleInstance, typeof(ILoggerService))]
[Register(typeof(Startup))]
public partial class TopLevelContainer : IContainer<Startup> {
    [Instance]
    private Configuration _configuration;

    public TopLevelContainer(Configuration configuration) => _configuration = configuration;
}
