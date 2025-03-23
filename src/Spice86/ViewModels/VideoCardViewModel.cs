namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Infrastructure;
using Spice86.Mappers;
using Spice86.Models.Debugging;

public partial class VideoCardViewModel  : ViewModelBase, IEmulatorObjectViewModel {
    [ObservableProperty]
    private VideoCardInfo _videoCard = new();
    private readonly IVgaRenderer _vgaRenderer;
    private readonly IVideoState _videoState;
    
    public VideoCardViewModel(IVgaRenderer vgaRenderer, IVideoState videoState) {
        _vgaRenderer = vgaRenderer;
        _videoState = videoState;
    }

    public bool IsVisible { get; set; }

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