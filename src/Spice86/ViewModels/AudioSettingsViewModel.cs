namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
/// Read-only view model displaying current audio hardware settings.
/// Settings are configured at startup via CLI options.
/// </summary>
public partial class AudioSettingsViewModel : ViewModelBase {
    /// <summary>
    /// Gets the current OPL mode.
    /// </summary>
    [ObservableProperty]
    private OplMode _oplMode;

    /// <summary>
    /// Gets the current Sound Blaster type.
    /// </summary>
    [ObservableProperty]
    private SbType _sbType;

    /// <summary>
    /// Gets the current Sound Blaster base address.
    /// </summary>
    [ObservableProperty]
    private ushort _sbBase;

    /// <summary>
    /// Gets the current Sound Blaster IRQ.
    /// </summary>
    [ObservableProperty]
    private byte _sbIrq;

    /// <summary>
    /// Gets the current Sound Blaster 8-bit DMA channel.
    /// </summary>
    [ObservableProperty]
    private byte _sbDma;

    /// <summary>
    /// Gets the current Sound Blaster 16-bit DMA channel.
    /// </summary>
    [ObservableProperty]
    private byte _sbHdma;

    /// <summary>
    /// Gets the current BLASTER environment variable string.
    /// </summary>
    [ObservableProperty]
    private string _blasterString = string.Empty;

    /// <summary>
    /// Gets whether AdLib Gold is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _isAdlibGoldEnabled;

    /// <summary>
    /// Gets the BLASTER environment variable format description.
    /// </summary>
    public string BlasterFormatString { get; } = "Format: A[base] I[irq] D[dma] H[hdma] T[type]";

    public AudioSettingsViewModel(SoundBlaster soundBlaster, Opl opl) {
        SbType = soundBlaster.SbTypeProperty;
        SbIrq = soundBlaster.IRQ;
        SbBase = soundBlaster.BaseAddress;
        SbDma = soundBlaster.LowDma;
        SbHdma = soundBlaster.HighDma;
        OplMode = opl.Mode;
        BlasterString = soundBlaster.BlasterString;
        IsAdlibGoldEnabled = opl.IsAdlibGoldEnabled;
    }
}
