namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using System;
using System.Linq;

/// <summary>
/// Resolves the <c>--GeneratedCode</c> comma list into the generator settings. Each entry is
/// <c>+name</c> or <c>-name</c> (no sign means <c>+</c>), applied left to right. The
/// <see cref="Table"/> is the one list of the valid names: parsing, the help text and the error
/// message all read it.
/// </summary>
internal static class GeneratedCodeSelection {
    /// <summary>The settings one list entry can change. The defaults are the production ones:
    /// device operations on, every instrumentation and relaxation off.</summary>
    public sealed record Resolved {
        /// <summary>Whether every instruction keeps its <c>// SSSS:OOOO asm</c> comment.</summary>
        public bool AsmComments { get; set; }

        /// <summary>Whether a <c>ServiceCalled</c> statement is printed before each direct service call.</summary>
        public bool ServiceCallNotifications { get; set; }

        /// <summary>Whether every branch condition and switch value is wrapped in a probe.</summary>
        public bool BranchProbes { get; set; }

        /// <summary>Whether a device protocol port sequence prints as one device call. On by default.</summary>
        public bool DeviceOperations { get; set; } = true;

        /// <summary>The relaxations of the generation policy.</summary>
        public GenerationRelaxation Relaxations { get; set; } = GenerationRelaxation.None;
    }

    /// <summary>The valid entry names and the setting each one drives. <c>CheckBuild</c> is a preset:
    /// it sets the instrumentation entries at once, and a later entry may override them.</summary>
    private static readonly (string Name, Action<Resolved, bool> Apply)[] Table = [
        ("AsmComments", (state, on) => state.AsmComments = on),
        ("CheckBuild", (state, on) => {
            state.ServiceCallNotifications = on;
            state.BranchProbes = on;
            state.DeviceOperations = !on;
        }),
        ("BranchProbes", (state, on) => state.BranchProbes = on),
        ("ServiceCallNotifications", (state, on) => state.ServiceCallNotifications = on),
        ("DeviceOperations", (state, on) => state.DeviceOperations = on),
        ("AssumeUnknownMemoryIsRam", (state, on) => SetRelaxation(state, GenerationRelaxation.UnknownMemoryIsRam, on)),
        ("AssumeAccessesStayInSegment", (state, on) => SetRelaxation(state, GenerationRelaxation.AccessesStayInSegment, on)),
        ("AssumeAbandonedStackSlotsUnobserved", (state, on) => SetRelaxation(state, GenerationRelaxation.AbandonedStackSlotsUnobserved, on)),
        ("AssumeSavedSlotsWrittenOnlyByPops", (state, on) => SetRelaxation(state, GenerationRelaxation.SavedSlotsWrittenOnlyByPops, on))
    ];

    /// <summary>The valid entry names, for the help text and the error message.</summary>
    public static string ValidNames => string.Join(", ", Table.Select(entry => entry.Name));

    private static void SetRelaxation(Resolved state, GenerationRelaxation relaxation, bool on) {
        state.Relaxations = on ? state.Relaxations | relaxation : state.Relaxations & ~relaxation;
    }

    /// <summary>Resolves the comma list into the generator settings.</summary>
    /// <param name="list">The option value: entries separated by commas, each <c>+name</c>,
    /// <c>-name</c> or <c>name</c>. Null or empty means the defaults.</param>
    /// <returns>The resolved settings.</returns>
    /// <exception cref="FormatException">An entry is not a valid sign and name, or the name is
    /// unknown. The message lists the valid names.</exception>
    public static Resolved Resolve(string? list) {
        Resolved state = new();
        if (string.IsNullOrWhiteSpace(list)) {
            return state;
        }
        foreach (string rawEntry in list.Split(',')) {
            string entry = rawEntry.Trim();
            bool on = true;
            if (entry.StartsWith('+')) {
                entry = entry[1..];
            } else if (entry.StartsWith('-')) {
                on = false;
                entry = entry[1..];
            }
            (string Name, Action<Resolved, bool> Apply) match = Table.FirstOrDefault(row => row.Name == entry);
            if (entry.Length == 0 || entry.StartsWith('+') || entry.StartsWith('-') || match.Name is null) {
                throw new FormatException(
                    $"Invalid --GeneratedCode entry '{rawEntry.Trim()}'. Valid names: {ValidNames}. " +
                    "Prefix with + or - to enable or disable, no sign enables.");
            }
            match.Apply(state, on);
        }
        return state;
    }
}
