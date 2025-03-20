namespace Spice86.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;

/// <summary>
/// Interface for the RegistersViewModel to support proper MVVM separation.
/// This interface defines the contract between the RegistersView and its ViewModel.
/// </summary>
public interface IRegistersViewModel : INotifyPropertyChanged {
    /// <summary>
    /// Gets the general purpose registers.
    /// </summary>
    ObservableCollection<RegisterViewModel> GeneralRegisters { get; }

    /// <summary>
    /// Gets the segment registers.
    /// </summary>
    ObservableCollection<RegisterViewModel> SegmentRegisters { get; }

    /// <summary>
    /// Gets the pointer registers.
    /// </summary>
    ObservableCollection<RegisterViewModel> PointerRegisters { get; }

    /// <summary>
    /// Gets the CPU flags.
    /// </summary>
    ObservableCollection<FlagViewModel> Flags { get; }

    /// <summary>
    /// Updates all register values from the CPU state.
    /// </summary>
    void Update();

    /// <summary>
    /// Resets the change detection for all registers.
    /// </summary>
    void ResetChangeDetection();
}
