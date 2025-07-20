namespace Spice86.ViewModels.Messages;

using System.ComponentModel;

public record AddViewModelMessage<T>(T ViewModel) where T : INotifyPropertyChanged;