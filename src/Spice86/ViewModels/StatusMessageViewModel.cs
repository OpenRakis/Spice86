namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Infrastructure;
using Spice86.Messages;

public partial class StatusMessageViewModel : ViewModelBase, IRecipient<StatusMessage> {
    private readonly IUIDispatcher _uiDispatcher;

    [ObservableProperty]
    private StatusMessage? _message;

    [ObservableProperty]
    private bool _isVisible;

    public StatusMessageViewModel(IUIDispatcher dispatcher, IMessenger messenger) {
        messenger.Register(this);
        _uiDispatcher = dispatcher;
    }

    public void Receive(StatusMessage message) {
        Message = message;
        IsVisible = true;
        Task.Delay(millisecondsDelay: 5000).ContinueWith(_ => {
            _uiDispatcher.Post(() => IsVisible = false);
        });
    }
}