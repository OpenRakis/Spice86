namespace Spice86.Tests;

using Spice86.Core.Emulator.VM;

internal sealed class GeneratedCodeRunOptions {
    public long MaxCycles { get; init; } = 100000;
    public bool EnablePit { get; init; }
    public bool EnableA20Gate { get; init; }
    public bool InstallInterruptVectors { get; init; }
    public bool FailOnUnhandledPort { get; init; }
    /// <summary>
    /// Optional hook invoked on the freshly created machine before the program runs, for both the discovery
    /// run and the generated-code run. Used to install custom I/O port handlers (e.g. the test386 POST port).
    /// </summary>
    public Action<Machine>? ConfigureMachine { get; init; }
}
