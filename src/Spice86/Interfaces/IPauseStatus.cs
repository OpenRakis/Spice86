namespace Spice86.Interfaces;

using System.ComponentModel;

public interface IPauseStatus : INotifyPropertyChanged {
    bool IsPaused { get; set; }
}