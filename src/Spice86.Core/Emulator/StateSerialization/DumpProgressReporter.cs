namespace Spice86.Core.Emulator.StateSerialization;

using Microsoft.Extensions.Logging;

using Spice86.Shared.Interfaces;

using System.Diagnostics;

/// <summary>
/// The stages of a recorded-data dump, in the order they run. File phases carry the dump file name in
/// their label; code-generation phases are reported by the C# generator itself.
/// </summary>
public enum DumpPhase {
    /// <summary>Writing the CPU registers JSON dump.</summary>
    CpuRegisters,

    /// <summary>Writing the memory dump.</summary>
    Memory,

    /// <summary>Writing the assembly listing dump.</summary>
    Listing,

    /// <summary>Building the CFG snapshot shared by the JSON and C# dumps.</summary>
    CfgSnapshot,

    /// <summary>Writing the CFG block graph JSON dump.</summary>
    CfgBlocks,

    /// <summary>Writing the CFG partition overlay JSON dump.</summary>
    CfgPartitions,

    /// <summary>Writing the CFG reload JSON dump.</summary>
    CfgReload,

    /// <summary>Writing the execution flow JSON dump.</summary>
    ExecutionFlow,

    /// <summary>Code generation: program analysis.</summary>
    CodeAnalysis,

    /// <summary>Code generation: planning and emitter setup.</summary>
    CodePlan,

    /// <summary>Code generation: per-method emission; progress is reported through steps.</summary>
    CodeMethods,

    /// <summary>Writing the generated C# source file.</summary>
    CodeSource,

    /// <summary>Writing the self-contained generated project.</summary>
    CodeProject,

    /// <summary>Writing the Ghidra symbols dump.</summary>
    Symbols,

    /// <summary>Writing the breakpoints JSON dump.</summary>
    Breakpoints
}

/// <summary>
/// Reports dump progress to the console log and to UI subscribers. Every emitted text is logged once at
/// Information level and raised through <see cref="MessageLogged"/> with the same text, so the console
/// log and the progress window always show identical lines.
/// </summary>
public sealed class DumpProgressReporter {
    private static readonly IReadOnlyDictionary<DumpPhase, string> PhaseLabels =
        new Dictionary<DumpPhase, string> {
            [DumpPhase.CpuRegisters] = "CPU registers dump",
            [DumpPhase.Memory] = "Memory dump",
            [DumpPhase.Listing] = "Assembly listing",
            [DumpPhase.CfgSnapshot] = "CFG snapshot",
            [DumpPhase.CfgBlocks] = "CFG blocks graph",
            [DumpPhase.CfgPartitions] = "CFG partitions",
            [DumpPhase.CfgReload] = "CFG reload dump",
            [DumpPhase.ExecutionFlow] = "Execution flow",
            [DumpPhase.CodeAnalysis] = "Code generation: analysis",
            [DumpPhase.CodePlan] = "Code generation: planning",
            [DumpPhase.CodeMethods] = "Code generation: methods",
            [DumpPhase.CodeSource] = "Generated C# source",
            [DumpPhase.CodeProject] = "Generated project",
            [DumpPhase.Symbols] = "Ghidra symbols",
            [DumpPhase.Breakpoints] = "Breakpoints"
        };

    private static readonly IReadOnlyDictionary<DumpPhase, string> StepLabels =
        new Dictionary<DumpPhase, string> {
            [DumpPhase.CodeMethods] = "Code generation: method"
        };

    private readonly ILogger _logger;
    private readonly ITimeProvider _timeProvider;
    private readonly Stopwatch _dumpWatch = new();
    private readonly Stopwatch _phaseWatch = new();
    private DumpPhase _currentPhase;
    private string _phaseLabel = string.Empty;
    private int _lastLoggedTenth = -1;
    private DateTime _lastStepLineTime = DateTime.MinValue;
    private string _directory = string.Empty;

    /// <summary>Creates a reporter logging to <paramref name="logger"/>.</summary>
    public DumpProgressReporter(ILogger logger, ITimeProvider timeProvider) {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>Raised once when a dump starts, before the first message.</summary>
    public event Action? DumpStarted;

    /// <summary>Raised for every emitted line, with exactly the text written to the logger.</summary>
    public event Action<string>? MessageLogged;

    /// <summary>Raised for every reported step: phase, current item, total item count and the name of the item.</summary>
    public event Action<DumpPhase, int, int, string>? StepReported;

    /// <summary>Raised once when the dump ends, whether it succeeded or failed.</summary>
    public event Action? DumpFinished;

    /// <summary>Raised when a phase begins, so the UI can reset the progress bar.</summary>
    public event Action<DumpPhase>? PhaseBegan;

    /// <summary>Raised when a phase ends, for tests and diagnostics.</summary>
    internal event Action<DumpPhase>? PhaseEnded;

    /// <summary>The human-readable label of a phase, including the dump file name for file phases.</summary>
    internal static string LabelFor(DumpPhase phase) => PhaseLabels[phase];

    /// <summary>The one step text, logged by <see cref="Step"/> and shown by the progress window.</summary>
    internal static string StepText(DumpPhase phase, int current, int total, string item) =>
        $"{StepLabelFor(phase)} {current} / {total} ({current * 100 / total}%): {item}";

    /// <summary>Starts a dump into <paramref name="directory"/>.</summary>
    public void BeginDump(string directory) {
        _directory = directory;
        _dumpWatch.Restart();
        DumpStarted?.Invoke();
        Emit($"Saving recorded data to {directory}");
    }

    /// <summary>Begins <paramref name="phase"/>: logs its label and restarts the phase timer.</summary>
    public void BeginPhase(DumpPhase phase) => BeginPhaseCore(phase, LabelFor(phase));

    /// <summary>
    /// Begins <paramref name="phase"/> labelling it "{label} ({fileName})": used by the file-writing
    /// phases so the file name they produce appears in the started/finished lines.
    /// </summary>
    public void BeginPhase(DumpPhase phase, string fileName) =>
        BeginPhaseCore(phase, $"{LabelFor(phase)} ({fileName})");

    private void BeginPhaseCore(DumpPhase phase, string label) {
        _currentPhase = phase;
        _phaseLabel = label;
        _lastLoggedTenth = -1;
        _phaseWatch.Restart();
        _lastStepLineTime = DateTime.MinValue;
        PhaseBegan?.Invoke(phase);
        Emit($"{label}: started");
    }

    /// <summary>
    /// Reports step <paramref name="current"/> of <paramref name="total"/> in the current phase, naming
    /// <paramref name="item"/> as the work being done.
    /// <see cref="StepReported"/> is raised for every step; a line is logged only when the tenth of
    /// progress increases, when at least one second passed since the last step line of the current
    /// phase, plus always on the last step.
    /// </summary>
    public void Step(int current, int total, string item) {
        StepReported?.Invoke(_currentPhase, current, total, item);
        int tenth = current * 10 / total;
        DateTime now = _timeProvider.Now;
        bool secondPassed = now - _lastStepLineTime >= TimeSpan.FromSeconds(1);
        if (tenth <= _lastLoggedTenth && !secondPassed && current != total) {
            return;
        }
        _lastLoggedTenth = tenth;
        _lastStepLineTime = now;
        Emit(StepText(_currentPhase, current, total, item));
    }

    /// <summary>Logs <paramref name="message"/> at Information level and raises it through
    /// <see cref="MessageLogged"/>.</summary>
    public void Detail(string message) => Emit(message);

    /// <summary>Ends the current phase and logs its elapsed time.</summary>
    public void EndPhase() {
        Emit($"{_phaseLabel}: finished in {_phaseWatch.ElapsedMilliseconds} ms");
        PhaseEnded?.Invoke(_currentPhase);
    }

    /// <summary>Ends the dump and logs the total elapsed time.</summary>
    public void EndDump(bool succeeded) {
        Emit(succeeded
            ? $"Recorded data saved to {_directory} in {_dumpWatch.ElapsedMilliseconds} ms"
            : $"Saving recorded data to {_directory} failed after {_dumpWatch.ElapsedMilliseconds} ms");
        DumpFinished?.Invoke();
    }

    private static string StepLabelFor(DumpPhase phase) =>
        StepLabels.TryGetValue(phase, out string? label) ? label : $"{LabelFor(phase)}:";

    private void Emit(string message) {
        if (_logger.IsEnabled(LogLevel.Information)) {
            _logger.LogInformation("{Message}", message);
        }
        MessageLogged?.Invoke(message);
    }
}
