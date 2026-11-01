namespace Spice86.Tests.InterruptHandlers;

using Spice86.Tests.Utility;

/// <summary>
/// A stopped emulator instance with DOS and all interrupt vectors installed,
/// used by the service contract tests.
/// </summary>
public sealed class ServiceContractMachine : IDisposable {
    private readonly TempFile _exeDir;
    private readonly TempFile _driveDir;
    private readonly Spice86Creator _creator;

    public ServiceContractMachine(bool enableEms, bool enableXms) {
        _exeDir = new TempFile("ServiceContractExe");
        _driveDir = new TempFile("ServiceContractDrive");
        string exePath = Path.Join(_exeDir.Path, "EXIT.COM");
        File.Copy(Path.Join(AppContext.BaseDirectory, "Resources", "DosConsoleProgramTests", "exit_code_0.com"),
            exePath, overwrite: true);
        _creator = new Spice86Creator(
            binName: exePath,
            installInterruptVectors: true,
            enableXms: enableXms,
            enableEms: enableEms,
            cDrive: _driveDir.Path,
            instructionTimeScale: 333333,
            clockStartTime: new DateTimeOffset(1993, 6, 1, 12, 0, 0, TimeSpan.Zero),
            // The stub-mode contract assertions count emulated memory reads of the
            // service; speculative exploration fetches stub instructions ahead of their
            // execution, which would count as reads the service never made.
            enableSpeculativeCfgExploration: false);
        Spice86 = _creator.Create();
    }

    /// <summary>The created emulator instance.</summary>
    public Spice86DependencyInjection Spice86 { get; }

    /// <summary>The host folder mounted as drive C:.</summary>
    public string CDrivePath => _driveDir.Path;

    /// <inheritdoc />
    public void Dispose() {
        Spice86.Dispose();
        _creator.Dispose();
        _exeDir.Dispose();
        _driveDir.Dispose();
    }
}
