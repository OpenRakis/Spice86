namespace Spice86.ViewModels.ValueViewModels.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

using System.ComponentModel;

/// <summary>
/// Aggregate view of DOS memory availability across conventional, EMS and XMS pools.
/// </summary>
public partial class DosMemorySummaryInfo : InspectorInfoBase {
    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Conventional")]
    [property: DisplayName("Free conventional (bytes)")]
    private long _conventionalFreeBytes;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Conventional")]
    [property: DisplayName("Used conventional (bytes)")]
    private long _conventionalUsedBytes;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Conventional")]
    [property: DisplayName("Largest free MCB (bytes)")]
    private long _largestFreeBlockBytes;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Conventional")]
    [property: DisplayName("Total MCBs")]
    private int _mcbCount;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Conventional")]
    [property: DisplayName("Free MCBs")]
    private int _freeMcbCount;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("EMS")]
    [property: DisplayName("EMS enabled")]
    private bool _emsEnabled;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("EMS")]
    [property: DisplayName("EMS total pages (16 KB)")]
    private int _emsTotalPages;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("EMS")]
    [property: DisplayName("EMS free pages (16 KB)")]
    private int _emsFreePages;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("EMS")]
    [property: DisplayName("EMS allocated handles")]
    private int _emsAllocatedHandles;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("XMS")]
    [property: DisplayName("XMS enabled")]
    private bool _xmsEnabled;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("XMS")]
    [property: DisplayName("XMS total (bytes)")]
    private long _xmsTotalBytes;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("XMS")]
    [property: DisplayName("XMS free (bytes)")]
    private long _xmsFreeBytes;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("XMS")]
    [property: DisplayName("XMS allocated handles")]
    private int _xmsAllocatedHandles;
}
