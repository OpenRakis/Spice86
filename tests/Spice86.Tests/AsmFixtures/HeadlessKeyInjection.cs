namespace Spice86.Tests.AsmFixtures;

using Spice86.ViewModels.Services;

/// <summary>
/// One keyboard injection into the headless GUI input pipeline: the run fires it once when the emulated
/// CPU reaches <see cref="CycleCount"/>, exactly as a user pressing keys while the program runs. The
/// injection itself stays a scenario concern (Ctrl-C, Ctrl-Break, ...); the runner only owns the timing
/// and the one-shot firing.
/// </summary>
/// <param name="CycleCount">CPU cycle count at which the injection fires once.</param>
/// <param name="InjectKeys">Injects the keys into the headless GUI of the running emulator.</param>
internal sealed record HeadlessKeyInjection(long CycleCount, Action<HeadlessGui> InjectKeys);
