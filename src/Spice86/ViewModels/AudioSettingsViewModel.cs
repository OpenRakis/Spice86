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

    /// <summary>
    /// Gets the description for the current OPL mode.
    /// </summary>
    public string OplModeDescription => OplMode switch {
        OplMode.Opl2 => "Yamaha OPL2 FM synthesizer",
        OplMode.Opl3 => "Yamaha OPL3 FM synthesizer",
        OplMode.DualOpl2 => "Dual OPL2 FM synthesizers",
        OplMode.Opl3Gold => "AdLib Gold OPL3 FM synthesizer",
        _ => "Unknown OPL mode"
    };

    /// <summary>
    /// Gets the description for the current Sound Blaster type.
    /// </summary>
    public string SbTypeDescription => SbType switch {
        SbType.None => "Sound Blaster disabled",
        SbType.SB1 => "Sound Blaster 1.0",
        SbType.SB2 => "Sound Blaster 2.0",
        SbType.SBPro1 => "Sound Blaster Pro (mono)",
        SbType.SBPro2 => "Sound Blaster Pro (stereo)",
        SbType.Sb16 => "Sound Blaster 16",
        SbType.GameBlaster => "Creative GameBlaster",
        _ => "Unknown Sound Blaster type"
    };

    public AudioSettingsViewModel(SoundBlaster soundBlaster, Opl3Fm opl) {
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
