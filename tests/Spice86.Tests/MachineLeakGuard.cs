[assembly: Xunit.AssemblyFixture(typeof(Spice86.Tests.MachineLeakGuard))]
// Every test may run in parallel with every other; tests sharing mutable state opt out individually.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.All)]

namespace Spice86.Tests;

using Xunit;

/// <summary>
/// Assembly-wide guard that runs once after every test in the assembly has completed. It fails the run if
/// any emulator instance created through <see cref="Spice86Creator"/> is still reachable after disposal and
/// garbage collection, which would indicate a reference leak pinning the whole Machine graph.
/// </summary>
public sealed class MachineLeakGuard : IDisposable {
    public void Dispose() {
        int tracked = MachineLeakTracker.TrackedCount;
        if (tracked == 0) {
            return;
        }

        List<string> surviving = MachineLeakTracker.SurvivingAfterCollection();
        Assert.True(surviving.Count == 0,
            $"{surviving.Count} of {tracked} emulator instances created via Spice86Creator were still alive after " +
            "disposal and garbage collection. This indicates a reference leak that keeps the entire Machine graph rooted. " +
            $"Created by: {string.Join("; ", surviving)}");
    }
}
