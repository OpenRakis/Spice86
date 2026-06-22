namespace Spice86.Tests;

using Spice86.Core.Emulator.VM;

/// <summary>
/// Tracks weak references to every <see cref="Machine"/> created through <see cref="Spice86Creator"/>.
/// A suite-wide check verifies that disposed emulator instances become collectible: a single rooted
/// Machine keeps its entire device, CFG and memory graph alive, and accumulating those across the suite
/// previously exhausted host memory.
/// </summary>
internal static class MachineLeakTracker {
    private static readonly List<WeakReference> TrackedMachines = new();
    private static readonly object SyncRoot = new();

    public static void Track(Machine machine) {
        lock (SyncRoot) {
            TrackedMachines.Add(new WeakReference(machine));
        }
    }

    public static int TrackedCount {
        get {
            lock (SyncRoot) {
                return TrackedMachines.Count;
            }
        }
    }

    public static int CountSurvivingAfterCollection() {
        for (int i = 0; i < 3; i++) {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        lock (SyncRoot) {
            return TrackedMachines.Count(reference => reference.IsAlive);
        }
    }
}
