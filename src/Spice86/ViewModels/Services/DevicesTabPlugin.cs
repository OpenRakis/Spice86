namespace Spice86.ViewModels.Services;

using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.ViewModels;

internal sealed class DevicesTabPlugin : IDebuggerTabPlugin {
    private readonly ArgbPalette _argbPalette;
    private readonly IUIDispatcher _uiDispatcher;
    private readonly IVgaRenderer _vgaRenderer;
    private readonly IVideoState _videoState;
    private readonly IHostStorageProvider _storageProvider;
    private readonly Midi _midi;

    public DevicesTabPlugin(ArgbPalette argbPalette, IUIDispatcher uiDispatcher, IVgaRenderer vgaRenderer,
        IVideoState videoState, IHostStorageProvider storageProvider, Midi midi) {
        _argbPalette = argbPalette;
        _uiDispatcher = uiDispatcher;
        _vgaRenderer = vgaRenderer;
        _videoState = videoState;
        _storageProvider = storageProvider;
        _midi = midi;
    }

    public void Register(IDebuggerTabRegistry registry) {
        VideoCardViewModel videoCardViewModel = new(_vgaRenderer, _videoState, _storageProvider);
        PaletteViewModel paletteViewModel = new(_argbPalette, _uiDispatcher);
        MidiViewModel midiViewModel = new(_midi);

        registry.AddSubTab(DebuggerTabIds.DevicesGroup,
            new DebuggerSubTabViewModel(DebuggerTabIds.DeviceVideoCard, "Video Card", videoCardViewModel));
        registry.AddSubTab(DebuggerTabIds.DevicesGroup,
            new DebuggerSubTabViewModel(DebuggerTabIds.DevicePalette, "Color Palette", paletteViewModel));
        registry.AddSubTab(DebuggerTabIds.DevicesGroup,
            new DebuggerSubTabViewModel(DebuggerTabIds.DeviceMidi, "General MIDI / MT-32", midiViewModel));
    }
}
