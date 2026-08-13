namespace Spice86.Core.CLI.RuntimeOptions;

/// <summary>
/// Mutable DOS runtime state shared by components that infer or consume DOS initialization behavior.
/// </summary>
public sealed class DosRuntimeState {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="installDosServices">Initial DOS services installation mode as parsed from command line.</param>
    public DosRuntimeState(bool? installDosServices) {
        InstallDosServices = installDosServices;
    }

    /// <summary>
    /// Gets or sets whether DOS interrupt vectors and related services should be installed.
    /// A value of <c>null</c> means the value should be inferred from the loaded program type.
    /// </summary>
    public bool? InstallDosServices { get; set; }
}