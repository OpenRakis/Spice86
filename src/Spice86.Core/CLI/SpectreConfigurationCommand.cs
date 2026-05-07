namespace Spice86.Core.CLI;

using Spectre.Console.Cli;

using System.Threading;

internal sealed class SpectreConfigurationCommand : Command<Configuration> {
    public static Configuration? LastParsedConfiguration { get; set; }

    protected override int Execute(CommandContext context, Configuration settings, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        LastParsedConfiguration = settings;
        return 0;
    }
}