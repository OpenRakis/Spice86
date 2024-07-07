namespace Spice86.DependencyInjection;

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.Messaging;

using Microsoft.Extensions.DependencyInjection;

using Spice86.Core.CLI;
using Spice86.Infrastructure;
using Spice86.Interfaces;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

public static class ServiceCollectionExtensions {
    public static void AddConfiguration(this IServiceCollection serviceCollection, string[] args) {
        serviceCollection.AddSingleton<ICommandLineParser, CommandLineParser>();
        serviceCollection.AddSingleton<Configuration>(serviceProvider => {
            ICommandLineParser commandLineParser = serviceProvider.GetRequiredService<ICommandLineParser>();

            return commandLineParser.ParseCommandLine(args);
        });
    }

    public static void AddLogging(this IServiceCollection serviceCollection) {
        serviceCollection.AddSingleton<ILoggerPropertyBag, LoggerPropertyBag>();
        serviceCollection.AddSingleton<ILoggerService, LoggerService>(serviceProvider => {
            Configuration configuration = serviceProvider.GetRequiredService<Configuration>();
            LoggerService loggerService = new LoggerService(serviceProvider.GetRequiredService<ILoggerPropertyBag>());
            Startup.SetLoggingLevel(loggerService, configuration);

            return loggerService;
        });
    }

    public static void AddGuiInfrastructure(this IServiceCollection serviceCollection, TopLevel mainWindow) {
        serviceCollection.AddSingleton<IAvaloniaKeyScanCodeConverter, AvaloniaKeyScanCodeConverter>();
        serviceCollection.AddSingleton<IWindowService, WindowService>();
        serviceCollection.AddSingleton<IUIDispatcher, UIDispatcher>(_ => new UIDispatcher(Dispatcher.UIThread));
        serviceCollection.AddSingleton<IUIDispatcherTimerFactory, UIDispatcherTimerFactory>();
        serviceCollection.AddSingleton<IStorageProvider>(_ => mainWindow.StorageProvider);
        serviceCollection.AddSingleton<IHostStorageProvider, HostStorageProvider>();
        serviceCollection.AddSingleton<ITextClipboard>(_ => new TextClipboard(mainWindow.Clipboard));
        serviceCollection.AddSingleton<IStructureViewModelFactory, StructureViewModelFactory>();
        serviceCollection.AddSingleton<IMessenger>(_ => WeakReferenceMessenger.Default);
    }
}