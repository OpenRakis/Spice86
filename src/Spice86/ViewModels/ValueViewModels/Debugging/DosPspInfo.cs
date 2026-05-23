namespace Spice86.ViewModels.ValueViewModels.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

using System.ComponentModel;

/// <summary>
/// Read-only snapshot of a DOS Program Segment Prefix (PSP).
/// </summary>
public partial class DosPspInfo : InspectorInfoBase {
    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Identity")]
    [property: DisplayName("Segment")]
    private string _segment = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Identity")]
    [property: DisplayName("Is current PSP")]
    private bool _isCurrent;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Identity")]
    [property: DisplayName("Parent PSP segment")]
    private string _parentSegment = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Identity")]
    [property: DisplayName("Previous PSP address")]
    private string _previousPspAddress = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Identity")]
    [property: DisplayName("Owner program name")]
    private string _ownerName = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Layout")]
    [property: DisplayName("Current size (paragraphs)")]
    private int _currentSizeParagraphs;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Layout")]
    [property: DisplayName("Environment segment")]
    private string _environmentSegment = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Layout")]
    [property: DisplayName("Saved SS:SP")]
    private string _stackPointer = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Layout")]
    [property: DisplayName("Max open files (JFT size)")]
    private int _maxOpenFiles;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Layout")]
    [property: DisplayName("File table address")]
    private string _fileTableAddress = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Exit vectors")]
    [property: DisplayName("Terminate (INT 22h)")]
    private string _terminateAddress = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Exit vectors")]
    [property: DisplayName("Ctrl-Break (INT 23h)")]
    private string _breakAddress = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Exit vectors")]
    [property: DisplayName("Critical error (INT 24h)")]
    private string _criticalErrorAddress = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("DOS")]
    [property: DisplayName("DOS version (major)")]
    private byte _dosVersionMajor;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("DOS")]
    [property: DisplayName("DOS version (minor)")]
    private byte _dosVersionMinor;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Command line")]
    [property: DisplayName("Command tail length")]
    private int _commandTailLength;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Command line")]
    [property: DisplayName("Command tail text")]
    private string _commandTailText = string.Empty;

    [ObservableProperty]
    [property: ReadOnly(true)]
    [property: Category("Command line")]
    [property: DisplayName("Environment variables preview")]
    private string _environmentVariablesPreview = string.Empty;
}
