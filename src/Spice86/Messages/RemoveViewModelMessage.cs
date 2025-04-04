namespace Spice86.Messages;

using System.ComponentModel;

public record RemoveViewModelMessage<T>(T ViewModel) where T : INotifyPropertyChanged;