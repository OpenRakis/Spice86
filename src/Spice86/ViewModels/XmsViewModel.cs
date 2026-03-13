namespace Spice86.ViewModels;

using Avalonia.Collections;

using AvaloniaHex.Document;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;
using Spice86.ViewModels.DataModels;

using System.Text;
using System.Windows.Input;

public partial class XmsViewModel : ViewModelBase, IEmulatorObjectViewModel, IMemorySearchViewModel, IDebuggerTabContentViewModel {

    private readonly ExtendedMemoryManager _xms;
    private readonly IPauseHandler _pauseHandler;
    private List<XmsHandleEntryViewModel> _allHandles = new();
    private List<XmsBlockEntryViewModel> _allBlocks = new();

    public XmsViewModel(ExtendedMemoryManager xms, IPauseHandler pauseHandler) {
        _xms = xms;
        _pauseHandler = pauseHandler;
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

    public enum MemorySearchDataType {
        Binary,
        Ascii,
    }

    [ObservableProperty]
    private MemorySearchDataType _searchDataType = MemorySearchDataType.Binary;

    public bool SearchDataTypeIsBinary => SearchDataType == MemorySearchDataType.Binary;

    public bool SearchDataTypeIsAscii => SearchDataType == MemorySearchDataType.Ascii;

    [RelayCommand]
    private void SetSearchDataTypeToBinary() => SetSearchDataType(MemorySearchDataType.Binary);

    [RelayCommand]
    private void SetSearchDataTypeToAscii() => SetSearchDataType(MemorySearchDataType.Ascii);

    [ObservableProperty]
    private string? _memorySearchValue;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private uint? _addressOFoundOccurence;

    [ObservableProperty]
    private bool _isAddressOfFoundOccurrenceValid;

    [ObservableProperty]
    private string _foundOccurrenceDisplay = "-";

    [ObservableProperty]
    private bool _isSearchingMemory;

    public ICommand SearchCancelCommand => SearchSelectedBlockCancelCommand;

    public ICommand SetSearchDataTypeToBinaryAction => SetSearchDataTypeToBinaryCommand;

    public ICommand SetSearchDataTypeToAsciiAction => SetSearchDataTypeToAsciiCommand;

    public ICommand FirstOccurrenceAction => FirstOccurrenceCommand;

    public ICommand NextOccurrenceAction => NextOccurrenceCommand;

    public ICommand PreviousOccurrenceAction => PreviousOccurrenceCommand;

    public ICommand SearchCancelAction => SearchCancelCommand;

    public ICommand StartMemorySearchAction => StartMemorySearchCommand;

    public ICommand StopMemorySearchAction => StopMemorySearchCommand;

    public bool CanOpenFoundOccurrence => false;

    public bool ShowOpenFoundOccurrenceAction => false;

    public ICommand OpenFoundOccurrenceCommand => StopMemorySearchCommand;

    public ICommand OpenFoundOccurrenceAction => OpenFoundOccurrenceCommand;

    private enum SearchDirection {
        FirstOccurence,
        Forward,
        Backward,
    }

    private SearchDirection _searchDirection;

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

    [RelayCommand]
    private async Task PreviousOccurrence() {
        _searchDirection = SearchDirection.Backward;
        await SearchSelectedBlockCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task NextOccurrence() {
        _searchDirection = SearchDirection.Forward;
        await SearchSelectedBlockCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task FirstOccurrence() {
        _searchDirection = SearchDirection.FirstOccurence;
        await SearchSelectedBlockCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void StartMemorySearch() {
        if (IsSearchingMemory) {
            OnPropertyChanged(nameof(IsSearchingMemory));
            return;
        }
        IsSearchingMemory = true;
    }

    [RelayCommand]
    private void StopMemorySearch() {
        if (IsBusy && SearchSelectedBlockCancelCommand.CanExecute(null)) {
            SearchSelectedBlockCancelCommand.Execute(null);
        }
        IsSearchingMemory = false;
    }

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

    private void SetSearchDataType(MemorySearchDataType searchDataType) {
        SearchDataType = searchDataType;
        OnPropertyChanged(nameof(SearchDataTypeIsBinary));
        OnPropertyChanged(nameof(SearchDataTypeIsAscii));
    }

    public void UpdateValues(object? sender, EventArgs e) {
        if (!IsVisible || !_pauseHandler.IsPaused) {
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
            ResetSearchResult();
            return;
        }

        if (SelectedBlock.Length == 0) {
            SelectedBlockDocument = null;
            SelectedBlockTitle = "Selected block is empty";
            ResetSearchResult();
            return;
        }

        SelectedBlockDocument = new XmsBlockBinaryDocument(_xms.XmsRam, SelectedBlock.Offset, SelectedBlock.Length);
        string handleText = SelectedBlock.IsFree ? "Free" : ConvertUtils.ToHex16((ushort)SelectedBlock.Handle);
        SelectedBlockTitle = $"{SelectedBlock.State} block - Handle {handleText} - {ConvertUtils.ToHex32(SelectedBlock.Offset)} to {ConvertUtils.ToHex32(SelectedBlock.EndOffset)}";
        ResetSearchResult();
    }

    [RelayCommand(FlowExceptionsToTaskScheduler = true, IncludeCancelCommand = true)]
    private async Task SearchSelectedBlock(CancellationToken token) {
        if (SelectedBlockDocument is null || SelectedBlock is null || string.IsNullOrWhiteSpace(MemorySearchValue) || token.IsCancellationRequested) {
            return;
        }

        byte[]? searchBytes = ParseSearchBytes();
        if (searchBytes is null || searchBytes.Length == 0) {
            return;
        }

        byte[] blockBytes = ReadSelectedBlockBytes();
        if (blockBytes.Length == 0 || blockBytes.Length < searchBytes.Length) {
            AddressOFoundOccurence = null;
            FoundOccurrenceDisplay = "Not found";
            IsAddressOfFoundOccurrenceValid = false;
            return;
        }

        try {
            IsBusy = true;
            int? found = await Task.Run(() => FindOccurrence(blockBytes, searchBytes, token), token);
            if (found is null) {
                AddressOFoundOccurence = null;
                FoundOccurrenceDisplay = "Not found";
                IsAddressOfFoundOccurrenceValid = false;
                return;
            }

            uint localOffset = (uint)found.Value;
            uint absoluteOffset = SelectedBlock.Offset + localOffset;
            AddressOFoundOccurence = localOffset;
            FoundOccurrenceDisplay = $"{ConvertUtils.ToHex32(localOffset)} (absolute {ConvertUtils.ToHex32(absoluteOffset)})";
            IsAddressOfFoundOccurrenceValid = true;
        } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            return;
        } finally {
            IsBusy = false;
        }
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

    private void ResetSearchResult() {
        AddressOFoundOccurence = null;
        FoundOccurrenceDisplay = "-";
        IsAddressOfFoundOccurrenceValid = false;
    }

    private byte[] ReadSelectedBlockBytes() {
        if (SelectedBlockDocument is null || SelectedBlockDocument.Length == 0 || SelectedBlockDocument.Length > int.MaxValue) {
            return [];
        }

        byte[] buffer = new byte[(int)SelectedBlockDocument.Length];
        SelectedBlockDocument.ReadBytes(0, buffer);
        return buffer;
    }

    private byte[]? ParseSearchBytes() {
        if (MemorySearchValue is null) {
            return null;
        }

        if (SearchDataType == MemorySearchDataType.Binary) {
            bool parsed = ConvertUtils.TryParseHexToByteArray(MemorySearchValue, out byte[]? searchBytes);
            return parsed ? searchBytes : null;
        }

        return Encoding.ASCII.GetBytes(MemorySearchValue);
    }

    private int? FindOccurrence(byte[] source, byte[] searchBytes, CancellationToken token) {
        if (searchBytes.Length == 0 || source.Length < searchBytes.Length) {
            return null;
        }

        if (_searchDirection == SearchDirection.Forward && AddressOFoundOccurence is not null) {
            int startIndex = (int)AddressOFoundOccurence.Value + 1;
            return FindForward(source, searchBytes, startIndex, token);
        }

        if (_searchDirection == SearchDirection.Backward) {
            int startIndex = source.Length - searchBytes.Length;
            if (AddressOFoundOccurence is not null && AddressOFoundOccurence.Value > 0) {
                startIndex = (int)AddressOFoundOccurence.Value - 1;
            }
            return FindBackward(source, searchBytes, startIndex, token);
        }

        return FindForward(source, searchBytes, 0, token);
    }

    private static int? FindForward(byte[] source, byte[] searchBytes, int startIndex, CancellationToken token) {
        if (startIndex < 0) {
            startIndex = 0;
        }

        int maxStart = source.Length - searchBytes.Length;
        for (int sourceIndex = startIndex; sourceIndex <= maxStart; sourceIndex++) {
            token.ThrowIfCancellationRequested();
            if (MatchesAt(source, searchBytes, sourceIndex, token)) {
                return sourceIndex;
            }
        }
        return null;
    }

    private static int? FindBackward(byte[] source, byte[] searchBytes, int startIndex, CancellationToken token) {
        int maxStart = source.Length - searchBytes.Length;
        if (startIndex > maxStart) {
            startIndex = maxStart;
        }

        for (int sourceIndex = startIndex; sourceIndex >= 0; sourceIndex--) {
            token.ThrowIfCancellationRequested();
            if (MatchesAt(source, searchBytes, sourceIndex, token)) {
                return sourceIndex;
            }
        }
        return null;
    }

    private static bool MatchesAt(byte[] source, byte[] searchBytes, int sourceIndex, CancellationToken token) {
        for (int patternIndex = 0; patternIndex < searchBytes.Length; patternIndex++) {
            token.ThrowIfCancellationRequested();
            if (source[sourceIndex + patternIndex] != searchBytes[patternIndex]) {
                return false;
            }
        }
        return true;
    }
}
