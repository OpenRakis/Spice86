namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.ViewModels.ValueViewModels.Debugging;
using Spice86.ViewModels.PropertiesMappers;
using Spice86.ViewModels.Services;

using System.Text.Json;

public partial class VideoCardViewModel : TimerRefreshViewModelBase
{
    public override string Header => "Video Card";
    [ObservableProperty]
    private VideoCardInfo _videoCard = new();
    private readonly IVgaRenderer _vgaRenderer;
    private readonly IVideoState _videoState;
    private readonly VgaTimingEngine _vgaTimingEngine;
    private readonly IHostStorageProvider _storageProvider;

    public VideoCardViewModel(IVgaRenderer vgaRenderer, IVideoState videoState,
        VgaTimingEngine vgaTimingEngine, IHostStorageProvider storageProvider)
        : base(400)
    {
        _vgaRenderer = vgaRenderer;
        _videoState = videoState;
        _vgaTimingEngine = vgaTimingEngine;
        _storageProvider = storageProvider;
    }

    [RelayCommand]
    public async Task SaveVideoCardInfo()
    {
        await _storageProvider.SaveVideoCardInfoFile(JsonSerializer.Serialize(VideoCard));
    }

    protected override void RefreshCore() {
        VisitVgaRenderer(_vgaRenderer);
        VisitVideoState(_videoState);
        VideoCard.LastFrameDuration = _vgaTimingEngine.LastFrameDuration;
    }

    private void VisitVgaRenderer(IVgaRenderer vgaRenderer)
    {
        vgaRenderer.CopyToVideoCardInfo(VideoCard);
    }

    private void VisitVideoState(IVideoState videoState)
    {
        videoState.CopyToVideoCardInfo(VideoCard);
    }
}