namespace Spice86.ViewModels;

using System;
using System.Collections.Generic;
using System.Linq;

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
    /// Gets the CLI options reference for audio configuration.
    /// </summary>
    public IReadOnlyList<CliOptionInfo> CliOptions { get; }

    public AudioSettingsViewModel(SoundBlaster soundBlaster, Opl opl) {
        SbType = soundBlaster.SbTypeProperty;
        SbIrq = soundBlaster.IRQ;
        SbBase = soundBlaster.BaseAddress;
        SbDma = soundBlaster.LowDma;
        SbHdma = soundBlaster.HighDma;
        OplMode = opl.Mode;
        BlasterString = soundBlaster.BlasterString;
        IsAdlibGoldEnabled = opl.IsAdlibGoldEnabled;
        CliOptions = BuildCliOptions();
    }

    private static IReadOnlyList<CliOptionInfo> BuildCliOptions() {
        string sbTypeValues = string.Join(", ", Enum.GetNames<SbType>());
        string oplModeValues = string.Join(", ", Enum.GetNames<OplMode>());
        return new List<CliOptionInfo> {
            new("--SbType", "Sound Blaster card type", sbTypeValues, nameof(SbType.SBPro2)),
            new("--OplMode", "OPL synthesis mode", oplModeValues, nameof(OplMode.Opl3)),
            new("--SbBase", "Sound Blaster base I/O address", "0x220, 0x240, 0x260, 0x280", "0x220"),
            new("--SbIrq", "Sound Blaster IRQ line", "5, 7, 9, 10", "7"),
            new("--SbDma", "Sound Blaster 8-bit DMA channel", "0, 1, 3", "1"),
            new("--SbHdma", "Sound Blaster 16-bit high DMA channel", "5, 6, 7", "5")
        };
    }
}
