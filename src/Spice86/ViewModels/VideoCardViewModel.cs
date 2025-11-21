namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.ViewModels.ValueViewModels.Debugging;
using Spice86.ViewModels.PropertiesMappers;
using Spice86.ViewModels.Services;

using System.Text.Json;

public partial class VideoCardViewModel  : ViewModelBase, IEmulatorObjectViewModel {
    [ObservableProperty]
    private VideoCardInfo _videoCard = new();
    private readonly IVgaRenderer _vgaRenderer;
    private readonly IVideoState _videoState;
    private readonly IHostStorageProvider _storageProvider;

    public VideoCardViewModel(IVgaRenderer vgaRenderer, IVideoState videoState,
        IHostStorageProvider storageProvider) {
        _vgaRenderer = vgaRenderer;
        _videoState = videoState;
        _storageProvider = storageProvider;
        IsVisible = true;
    }

    public bool IsVisible { get; set; }

    [RelayCommand]
    public async Task SaveVideoCardInfo() {
        await _storageProvider.SaveVideoCardInfoFile(JsonSerializer.Serialize(VideoCard));
    }

    public void UpdateValues(object? sender, EventArgs e) {
        if (!IsVisible) {
            return;
        }
        VisitVgaRenderer(_vgaRenderer);
        VisitVideoState(_videoState);
    }

    private void VisitVgaRenderer(IVgaRenderer vgaRenderer) {
        vgaRenderer.CopyToVideoCardInfo(VideoCard);
    }

    private void VisitVideoState(IVideoState videoState) {
        videoState.CopyToVideoCardInfo(VideoCard);
    }
}