namespace Spice86.Interfaces;

using Spice86.Core.Emulator.InternalDebugger;
using System.ComponentModel;

public interface IPauseStatus : INotifyPropertyChanged {
    bool IsPaused { get; set; }
}