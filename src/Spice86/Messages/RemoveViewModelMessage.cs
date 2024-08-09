namespace Spice86.Messages;

using Spice86.ViewModels;

public record RemoveViewModelMessage<T>(T ViewModel) where T : ViewModelBase;