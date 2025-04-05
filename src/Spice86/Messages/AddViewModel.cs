namespace Spice86.Messages;

using System.ComponentModel;

public record AddViewModelMessage<T>(T ViewModel) where T : INotifyPropertyChanged;