namespace Spice86.Tests;

using Spice86.Core.Emulator.VM;

using Xunit;

/// <summary>
/// Tracks weak references to every <see cref="Machine"/> created through <see cref="Spice86Creator"/>.
/// A suite-wide check verifies that disposed emulator instances become collectible: a single rooted
/// Machine keeps its entire device, CFG and memory graph alive, and accumulating those across the suite
/// previously exhausted host memory.
/// </summary>
internal static class MachineLeakTracker {
    private static readonly List<(WeakReference Machine, string CreatingTest)> TrackedMachines = new();
    private static readonly object SyncRoot = new();

    public static void Track(Machine machine) {
        string creatingTest = TestContext.Current.Test?.TestDisplayName ?? "unknown";
        lock (SyncRoot) {
            TrackedMachines.Add((new WeakReference(machine), creatingTest));
        }
    }

    public static int TrackedCount {
        get {
            lock (SyncRoot) {
                return TrackedMachines.Count;
            }
        }
    }

    /// <summary>Returns the name of the test that created each machine still alive after collection.</summary>
    public static List<string> SurvivingAfterCollection() {
        CollectUnreachable();
        lock (SyncRoot) {
            return TrackedMachines.Where(tracked => tracked.Machine.IsAlive).Select(tracked => tracked.CreatingTest).ToList();
        }
    }

    /// <summary>Runs the collector enough times for an unreachable machine graph to actually be reclaimed.</summary>
    public static void CollectUnreachable() {
        for (int i = 0; i < 3; i++) {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
