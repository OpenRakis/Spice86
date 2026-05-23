namespace Spice86.ViewModels.ValueViewModels.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

using System.ComponentModel;

/// <summary>
/// Read-only snapshot of a single DOS Memory Control Block (MCB).
/// </summary>
public partial class DosMcbInfo : InspectorInfoBase {
    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Identity")]
    [property: DisplayName("Header segment")]
    private string _headerSegment = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Identity")]
    [property: DisplayName("Data segment")]
    private string _dataSegment = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Identity")]
    [property: DisplayName("Type")]
    private string _type = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Identity")]
    [property: DisplayName("Type byte")]
    private byte _typeByte;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Ownership")]
    [property: DisplayName("Owner PSP segment")]
    private string _ownerPspSegment = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Ownership")]
    [property: DisplayName("Owner program name")]
    private string _ownerName = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Ownership")]
    [property: DisplayName("Is free")]
    private bool _isFree;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Ownership")]
    [property: DisplayName("Is last block")]
    private bool _isLast;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Ownership")]
    [property: DisplayName("Is valid")]
    private bool _isValid;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Size")]
    [property: DisplayName("Size (paragraphs)")]
    private int _sizeParagraphs;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Size")]
    [property: DisplayName("Size (bytes)")]
    private long _sizeBytes;
}
