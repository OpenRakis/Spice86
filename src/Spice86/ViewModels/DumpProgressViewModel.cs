namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.StateSerialization;

using System.Collections.ObjectModel;

/// <summary>
/// State of <see cref="Spice86.Views.DumpProgressWindow"/>: the current phase text, the progress bar
/// and the log lines emitted by the dump reporter.
/// </summary>
public partial class DumpProgressViewModel : ObservableObject {
    /// <summary>Text describing the activity currently in progress.</summary>
    [ObservableProperty] private string _phaseText = string.Empty;

    /// <summary>Current progress value while steps are being reported.</summary>
    [ObservableProperty] private double _progressValue;

    /// <summary>Maximum of the progress bar: always 1, steps map to a fraction of it.</summary>
    [ObservableProperty] private double _progressMaximum = 1;

    /// <summary>True while no step has been reported yet in the current phase.</summary>
    [ObservableProperty] private bool _isIndeterminate = true;

    /// <summary>True once the dump finished; the window may then close.</summary>
    [ObservableProperty] private bool _isFinished;

    /// <summary>Every line emitted by the dump reporter, in order.</summary>
    public ObservableCollection<string> Messages { get; } = new();

    /// <summary>Appends a logged line; while no step has been reported yet in the current phase, it
    /// also becomes the phase text.</summary>
    public void AddMessage(string message) {
        Messages.Add(message);
        if (IsIndeterminate) {
            PhaseText = message;
        }
    }

    /// <summary>Reports step <paramref name="current"/> of <paramref name="total"/> in <paramref name="phase"/>, naming <paramref name="item"/>.</summary>
    public void ReportStep(DumpPhase phase, int current, int total, string item) {
        IsIndeterminate = false;
        ProgressValue = DumpProgressReporter.ProgressFraction(phase, current, total);
        PhaseText = DumpProgressReporter.StepText(phase, current, total, item);
    }

    /// <summary>Resets the progress bar to indeterminate when a new phase begins, unless the phase
    /// continues the shared code-generation bar.</summary>
    public void BeginPhase(DumpPhase phase) {
        if (DumpProgressReporter.ContinuesProgress(phase)) {
            return;
        }
        IsIndeterminate = true;
        ProgressValue = 0;
    }

    /// <summary>Marks the dump as finished; the window may then close.</summary>
    public void Finish() => IsFinished = true;
}
