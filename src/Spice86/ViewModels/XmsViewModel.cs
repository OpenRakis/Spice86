namespace Spice86.ViewModels;

using Avalonia.Collections;

using AvaloniaHex.Document;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Shared.Utils;
using Spice86.ViewModels.DataModels;

public partial class XmsViewModel : ViewModelBase, IEmulatorObjectViewModel {
    private readonly ExtendedMemoryManager _xms;
    private List<XmsHandleEntryViewModel> _allHandles = new();
    private List<XmsBlockEntryViewModel> _allBlocks = new();

    public XmsViewModel(ExtendedMemoryManager xms) {
        _xms = xms;
        Refresh();
    }

    public bool IsVisible { get; set; }

    [ObservableProperty]
    private string _header = "XMS";

    [ObservableProperty]
    private uint _totalMemoryBytes;

    [ObservableProperty]
    private long _totalFreeBytes;

    [ObservableProperty]
    private uint _largestFreeBlockBytes;

    [ObservableProperty]
    private bool _isHighMemoryAreaClaimed;

    [ObservableProperty]
    private bool _isA20Enabled;

    [ObservableProperty]
    private bool _isA20GloballyEnabled;

    [ObservableProperty]
    private uint _a20LocalEnableCount;

    [ObservableProperty]
    private string _handleSearchText = string.Empty;

    [ObservableProperty]
    private string _blockSearchText = string.Empty;

    [ObservableProperty]
    private AvaloniaList<XmsHandleEntryViewModel> _handles = new();

    [ObservableProperty]
    private XmsHandleEntryViewModel? _selectedHandle;

    [ObservableProperty]
    private AvaloniaList<XmsBlockEntryViewModel> _blocks = new();

    [ObservableProperty]
    private XmsBlockEntryViewModel? _selectedBlock;

    [ObservableProperty]
    private IBinaryDocument? _selectedBlockDocument;

    [ObservableProperty]
    private string _selectedBlockTitle = "Select an XMS block";

    partial void OnHandleSearchTextChanged(string value) {
        int? selectedHandle = SelectedHandle?.Handle;
        ApplyHandleFilter(selectedHandle);
    }

    partial void OnBlockSearchTextChanged(string value) {
        uint? selectedOffset = SelectedBlock?.Offset;
        ApplyBlockFilter(selectedOffset);
    }

    partial void OnSelectedHandleChanged(XmsHandleEntryViewModel? value) {
        uint? selectedOffset = SelectedBlock?.Offset;
        ApplyBlockFilter(selectedOffset);
    }

    partial void OnSelectedBlockChanged(XmsBlockEntryViewModel? value) {
        RefreshSelectedBlockDocument();
    }

    public void UpdateValues(object? sender, EventArgs e) {
        if (!IsVisible) {
            return;
        }
        Refresh();
    }

    private void Refresh() {
        int? selectedHandle = SelectedHandle?.Handle;
        uint? selectedOffset = SelectedBlock?.Offset;

        TotalMemoryBytes = ExtendedMemoryManager.XmsMemorySize * 1024u;
        TotalFreeBytes = _xms.TotalFreeMemory;
        LargestFreeBlockBytes = _xms.LargestFreeBlockLength;
        IsHighMemoryAreaClaimed = _xms.IsHighMemoryAreaClaimed;
        IsA20Enabled = _xms.IsA20Enabled;
        IsA20GloballyEnabled = _xms.IsA20GloballyEnabled;
        A20LocalEnableCount = _xms.A20LocalEnableCount;

        _allHandles = _xms.HandlesSnapshot
            .Select(static x => new XmsHandleEntryViewModel(x.Key, x.Value))
            .ToList();

        _allBlocks = _xms.BlocksSnapshot
            .Select(static x => new XmsBlockEntryViewModel(x.IsFree, x.Handle, x.Offset, x.Length))
            .OrderBy(static x => x.Offset)
            .ToList();

        ApplyHandleFilter(selectedHandle);
        ApplyBlockFilter(selectedOffset);
    }

    private void ApplyHandleFilter(int? selectedHandle) {
        IEnumerable<XmsHandleEntryViewModel> filteredHandles = _allHandles;
        string trimmedSearch = HandleSearchText.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedSearch)) {
            filteredHandles = filteredHandles.Where(x => HandleMatchesSearch(x, trimmedSearch));
        }

        Handles = new AvaloniaList<XmsHandleEntryViewModel>(filteredHandles);
        SelectedHandle = Handles.FirstOrDefault(x => x.Handle == selectedHandle) ?? Handles.FirstOrDefault();
    }

    private void ApplyBlockFilter(uint? selectedOffset) {
        IEnumerable<XmsBlockEntryViewModel> filteredBlocks = _allBlocks;

        if (SelectedHandle is not null) {
            int handle = SelectedHandle.Handle;
            filteredBlocks = filteredBlocks.Where(x => x.IsFree || x.Handle == handle);
        }

        string trimmedSearch = BlockSearchText.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedSearch)) {
            filteredBlocks = filteredBlocks.Where(x => BlockMatchesSearch(x, trimmedSearch));
        }

        Blocks = new AvaloniaList<XmsBlockEntryViewModel>(filteredBlocks);
        SelectedBlock = Blocks.FirstOrDefault(x => x.Offset == selectedOffset) ?? Blocks.FirstOrDefault();
    }

    private void RefreshSelectedBlockDocument() {
        if (SelectedBlock is null) {
            SelectedBlockDocument = null;
            SelectedBlockTitle = "Select an XMS block";
            return;
        }

        if (SelectedBlock.Length == 0) {
            SelectedBlockDocument = null;
            SelectedBlockTitle = "Selected block is empty";
            return;
        }

        SelectedBlockDocument = new XmsBlockBinaryDocument(_xms.XmsRam, SelectedBlock.Offset, SelectedBlock.Length);
        string handleText = SelectedBlock.IsFree ? "Free" : ConvertUtils.ToHex16((ushort)SelectedBlock.Handle);
        SelectedBlockTitle = $"{SelectedBlock.State} block - Handle {handleText} - {ConvertUtils.ToHex32(SelectedBlock.Offset)} to {ConvertUtils.ToHex32(SelectedBlock.EndOffset)}";
    }

    private static bool HandleMatchesSearch(XmsHandleEntryViewModel handle, string search) {
        string loweredSearch = search.ToLowerInvariant();
        return handle.Handle.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
            ConvertUtils.ToHex16((ushort)handle.Handle).ToLowerInvariant().Contains(loweredSearch);
    }

    private static bool BlockMatchesSearch(XmsBlockEntryViewModel block, string search) {
        string loweredSearch = search.ToLowerInvariant();
        return block.State.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            block.Handle.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
            block.Offset.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
            block.EndOffset.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
            ConvertUtils.ToHex32(block.Offset).ToLowerInvariant().Contains(loweredSearch) ||
            ConvertUtils.ToHex32(block.EndOffset).ToLowerInvariant().Contains(loweredSearch);
    }
}
