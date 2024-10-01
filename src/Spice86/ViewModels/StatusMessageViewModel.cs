namespace Spice86.ViewModels;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Messages;

public partial class StatusMessageViewModel : ViewModelBase, IRecipient<StatusMessage> {
    [ObservableProperty]
    private AvaloniaList<StatusMessage> _previousMessages = new();
    
    [ObservableProperty]
    private StatusMessage? _message;

    public StatusMessageViewModel(IMessenger messenger) => messenger.Register(this);

    public void Receive(StatusMessage message) {
        PreviousMessages.Add(message);
        Message = message;
        if (PreviousMessages.Count > 50)
            PreviousMessages.RemoveAt(0);
    }
}