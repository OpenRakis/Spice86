namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Messages;

public partial class StatusMessageViewModel : ViewModelBase, IRecipient<StatusMessage> {
    [ObservableProperty]
    private StatusMessage? _message;

    public StatusMessageViewModel(IMessenger messenger) => messenger.Register(this);

    public void Receive(StatusMessage message) {
        Message = message;
    }
}