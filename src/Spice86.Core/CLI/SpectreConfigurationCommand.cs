namespace Spice86.Core.CLI;

using Spectre.Console.Cli;

internal sealed class SpectreConfigurationCommand : Command<Configuration> {
    public static Configuration? LastParsedConfiguration { get; set; }

    public override int Execute(CommandContext context, Configuration settings) {
        LastParsedConfiguration = settings;
        return 0;
    }
}