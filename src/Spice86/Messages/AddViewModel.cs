namespace Spice86.Messages;

using Spice86.ViewModels;

public record AddViewModelMessage<T>(T ViewModel) where T : ViewModelBase;