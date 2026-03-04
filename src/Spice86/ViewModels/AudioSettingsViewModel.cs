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
    /// Gets the OPL mode description.
    /// </summary>
    [ObservableProperty]
    private string _oplModeDescription = string.Empty;

    /// <summary>
    /// Gets the Sound Blaster type description.
    /// </summary>
    [ObservableProperty]
    private string _sbTypeDescription = string.Empty;

    public AudioSettingsViewModel(SoundBlaster soundBlaster, Opl opl) {
        SbType = soundBlaster.SbTypeProperty;
        SbIrq = soundBlaster.IRQ;
        SbBase = soundBlaster.BaseAddress;
        SbDma = soundBlaster.LowDma;
        SbHdma = soundBlaster.HighDma;
        OplMode = opl.Mode;
        BlasterString = soundBlaster.BlasterString;
        IsAdlibGoldEnabled = opl.IsAdlibGoldEnabled;

        OplModeDescription = GetOplModeDescription(OplMode);
        SbTypeDescription = GetSbTypeDescription(SbType);
    }

    private static string GetOplModeDescription(OplMode mode) {
        return mode switch {
            OplMode.None => "Disabled",
            OplMode.Opl2 => "OPL2 (Mono, 9 channels) - Original AdLib",
            OplMode.DualOpl2 => "Dual OPL2 (Stereo, 18 channels) - Sound Blaster Pro 1",
            OplMode.Opl3 => "OPL3 (Stereo, 18 channels, 4-op) - Sound Blaster Pro 2/16",
            OplMode.Opl3Gold => "OPL3 Gold (Stereo + Surround) - AdLib Gold 1000",
            _ => "Unknown"
        };
    }

    private static string GetSbTypeDescription(SbType sbType) {
        return sbType switch {
            SbType.None => "Disabled",
            SbType.SB1 => "Sound Blaster 1.0/1.5 (8-bit, mono, OPL2)",
            SbType.SB2 => "Sound Blaster 2.0 (8-bit, mono, OPL2, auto-init DMA)",
            SbType.SBPro1 => "Sound Blaster Pro (8-bit, stereo, Dual OPL2)",
            SbType.SBPro2 => "Sound Blaster Pro 2 (8-bit, stereo, OPL3)",
            SbType.Sb16 => "Sound Blaster 16 (16-bit, stereo, OPL3)",
            SbType.GameBlaster => "Creative Game Blaster (CMS chips, no OPL)",
            _ => "Unknown"
        };
    }
}
