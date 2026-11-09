namespace Spice86.Tests;

using System.Diagnostics;
using System.IO;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Tracks weak references to every <see cref="Machine"/> created through <see cref="Spice86Creator"/>.
/// A suite-wide check verifies that disposed emulator instances become collectible: a single rooted
/// Machine keeps its entire device, CFG and memory graph alive, and accumulating those across the suite
/// previously exhausted host memory.
/// </summary>
internal static class MachineLeakTracker {
    private sealed class TrackedMachine {
        public required WeakReference Machine { get; init; }
        public required StackTrace CreationStackTrace { get; init; }
    }

    private static readonly List<TrackedMachine> TrackedMachines = new();
    private static readonly object SyncRoot = new();

    public static void Track(Machine machine) {
        lock (SyncRoot) {
            TrackedMachines.Add(new TrackedMachine { Machine = new WeakReference(machine), CreationStackTrace = new StackTrace() });
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
        CollectUnreachable();
        lock (SyncRoot) {
            return TrackedMachines.Count(tracked => tracked.Machine.IsAlive);
        }
    }

    public static void ReportSurvivors(System.Text.StringBuilder output) {
        lock (SyncRoot) {
            foreach (TrackedMachine tracked in TrackedMachines) {
                if (!tracked.Machine.IsAlive) {
                    continue;
                }
                if (tracked.Machine.Target is object machine) {
                    output.AppendLine("Surviving machine created at:");
                    output.AppendLine(tracked.CreationStackTrace.ToString());
                    output.AppendLine();
                    output.AppendLine("Static fields referencing it:");
                    ScanStaticFields(machine, output);
                }
                output.AppendLine();
            }
        }
    }

    /// <summary>
    /// Builds the diagnostics for every surviving machine (creation stack plus the static fields
    /// that reference them) and writes them to a file, returning the file content.
    /// </summary>
    public static string WriteDiagnosticFile() {
        System.Text.StringBuilder output = new();
        ReportSurvivors(output);
        string path = Path.Join(FindRepositoryRoot(), "tmp", "machine-leak-diagnostic.txt");
        File.WriteAllText(path, output.ToString());
        return output.ToString();
    }

    private static void ScanStaticFields(object machine, System.Text.StringBuilder output) {
        foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
            System.Type[]? types;
            try {
                types = assembly.GetTypes();
            } catch (System.Reflection.ReflectionTypeLoadException ex) {
                types = ex.Types.Where(type => type is not null).Cast<System.Type>().ToArray();
            }
            if (types is null) {
                continue;
            }
            foreach (System.Type type in types) {
                if (type.IsGenericTypeDefinition) {
                    continue;
                }
                foreach (System.Reflection.FieldInfo field in type.GetFields(
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)) {
                    object? value;
                    try {
                        value = field.GetValue(null);
                    } catch (System.ArgumentException) {
                        continue;
                    }
                    if (ReferenceEquals(value, machine)) {
                        output.AppendLine($"  static {type.AssemblyQualifiedName}.{type.Name}.{field.Name}");
                        continue;
                    }
                    if (value is null) {
                        continue;
                    }
                    foreach (System.Reflection.FieldInfo inner in value.GetType().GetFields(
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)) {
                        if (inner.FieldType.IsValueType) {
                            continue;
                        }
                        object? innerValue;
                        try {
                            innerValue = inner.GetValue(value);
                        } catch (System.ArgumentException) {
                            continue;
                        }
                        if (ReferenceEquals(innerValue, machine)) {
                            output.AppendLine($"  static {type.AssemblyQualifiedName}.{type.Name}.{field.Name} -> {value.GetType().Name}.{inner.Name}");
                        }
                    }
                }
            }
        }
    }

    private static string FindRepositoryRoot() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && directory != null; i++) {
            if (Directory.Exists(Path.Join(directory.FullName, "tests"))) {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        return Path.GetTempPath();
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
