[assembly: Xunit.AssemblyFixture(typeof(Spice86.Tests.MachineLeakGuard))]

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

        int surviving = MachineLeakTracker.CountSurvivingAfterCollection();
        Assert.True(surviving == 0,
            $"{surviving} of {tracked} emulator instances created via Spice86Creator were still alive after " +
            "disposal and garbage collection. This indicates a reference leak that keeps the entire Machine graph rooted.");
    }
}
