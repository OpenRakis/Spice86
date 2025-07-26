namespace Spice86.ViewModels.Messages;

using System.ComponentModel;

public record RemoveViewModelMessage<T>(T ViewModel) where T : INotifyPropertyChanged;