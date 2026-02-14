namespace Spice86.ViewModels;

/// <summary>
/// Describes a single CLI option for display in the audio settings UI.
/// </summary>
/// <param name="OptionName">The CLI option flag name (e.g. "--SbType").</param>
/// <param name="Description">Short description of the option.</param>
/// <param name="ValidValues">Comma-separated list of valid values.</param>
/// <param name="DefaultValue">The default value for this option.</param>
public record CliOptionInfo(string OptionName, string Description, string ValidValues, string DefaultValue);
