namespace Spice86.ViewModels;

using Avalonia.Collections;

using AvaloniaHex.Document;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;
using Spice86.ViewModels.DataModels;

using System.Text;
using System.Windows.Input;

public partial class EmsViewModel : ViewModelBase, IEmulatorObjectViewModel, IMemorySearchViewModel, IDebuggerTabContentViewModel {
    private readonly ExpandedMemoryManager _ems;
    private readonly IPauseHandler _pauseHandler;
    private List<EmsHandleEntryViewModel> _allHandles = new();
    private List<EmsPhysicalPageEntryViewModel> _allPhysicalPages = new();

    public EmsViewModel(ExpandedMemoryManager ems, IPauseHandler pauseHandler) {
        _ems = ems;
        _pauseHandler = pauseHandler;
        Refresh();
    }

    public bool IsVisible { get; set; }

    [ObservableProperty]
    private string _header = "EMS";

    [ObservableProperty]
    private ushort _totalPages;

    [ObservableProperty]
    private ushort _freePages;

    [ObservableProperty]
    private ushort _pageSize;

    [ObservableProperty]
    private string _pageFrameSegment = string.Empty;

    [ObservableProperty]
    private string _handleSearchText = string.Empty;

    [ObservableProperty]
    private string _logicalPageSearchText = string.Empty;

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

    public ICommand SearchCancelCommand => SearchSelectedPageCancelCommand;

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
    private AvaloniaList<EmsHandleEntryViewModel> _handles = new();

    [ObservableProperty]
    private EmsHandleEntryViewModel? _selectedHandle;

    [ObservableProperty]
    private AvaloniaList<EmsLogicalPageEntryViewModel> _logicalPages = new();

    [ObservableProperty]
    private EmsLogicalPageEntryViewModel? _selectedLogicalPage;

    [ObservableProperty]
    private AvaloniaList<EmsPhysicalPageEntryViewModel> _physicalPages = new();

    [ObservableProperty]
    private IBinaryDocument? _selectedPageDocument;

    [ObservableProperty]
    private string _selectedPageTitle = "Select an EMS logical page";

    [RelayCommand]
    private async Task PreviousOccurrence() {
        _searchDirection = SearchDirection.Backward;
        await SearchSelectedPageCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task NextOccurrence() {
        _searchDirection = SearchDirection.Forward;
        await SearchSelectedPageCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task FirstOccurrence() {
        _searchDirection = SearchDirection.FirstOccurence;
        await SearchSelectedPageCommand.ExecuteAsync(null);
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
        if (IsBusy && SearchSelectedPageCancelCommand.CanExecute(null)) {
            SearchSelectedPageCancelCommand.Execute(null);
        }
        IsSearchingMemory = false;
    }

    partial void OnHandleSearchTextChanged(string value) {
        ushort? selectedHandleNumber = SelectedHandle?.HandleNumber;
        ApplyHandleFilter(selectedHandleNumber);
    }

    partial void OnLogicalPageSearchTextChanged(string value) {
        ushort? selectedPageNumber = SelectedLogicalPage?.PageNumber;
        ApplyLogicalPageFilter(selectedPageNumber);
    }

    public void UpdateValues(object? sender, EventArgs e) {
        if (!IsVisible || !_pauseHandler.IsPaused) {
            return;
        }
        Refresh();
    }

    partial void OnSelectedHandleChanged(EmsHandleEntryViewModel? value) {
        ushort? selectedPageNumber = SelectedLogicalPage?.PageNumber;
        ApplyLogicalPageFilter(selectedPageNumber);
    }

    partial void OnSelectedLogicalPageChanged(EmsLogicalPageEntryViewModel? value) {
        RefreshSelectedPageDocument();
    }

    private void SetSearchDataType(MemorySearchDataType searchDataType) {
        SearchDataType = searchDataType;
        OnPropertyChanged(nameof(SearchDataTypeIsBinary));
        OnPropertyChanged(nameof(SearchDataTypeIsAscii));
    }

    private void Refresh() {
        ushort? selectedHandleNumber = SelectedHandle?.HandleNumber;
        ushort? selectedPageNumber = SelectedLogicalPage?.PageNumber;

        TotalPages = EmmMemory.TotalPages;
        FreePages = (ushort)Math.Max(0, EmmMemory.TotalPages - _ems.EmmHandles.Sum(static x => x.Value.LogicalPages.Count));
        PageSize = ExpandedMemoryManager.EmmPageSize;
        PageFrameSegment = ConvertUtils.ToHex16(ExpandedMemoryManager.EmmPageFrameSegment);

        _allHandles = _ems.EmmHandles
            .OrderBy(static x => x.Key)
            .Select(static x => new EmsHandleEntryViewModel(
                (ushort)x.Key,
                x.Value.ToString(),
                x.Value.LogicalPages.Count,
                x.Value.SavedPageMap))
            .ToList();

        _allPhysicalPages = _ems.EmmPageFrame
            .OrderBy(static x => x.Key)
            .Select(x => CreatePhysicalPageEntry(x.Key, x.Value))
            .ToList();

        PhysicalPages = new AvaloniaList<EmsPhysicalPageEntryViewModel>(_allPhysicalPages);
        ApplyHandleFilter(selectedHandleNumber);
        ApplyLogicalPageFilter(selectedPageNumber);
    }

    private void ApplyHandleFilter(ushort? selectedHandleNumber) {
        IEnumerable<EmsHandleEntryViewModel> filteredHandles = _allHandles;
        string trimmedSearch = HandleSearchText.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedSearch)) {
            filteredHandles = filteredHandles.Where(x => HandleMatchesSearch(x, trimmedSearch));
        }

        Handles = new AvaloniaList<EmsHandleEntryViewModel>(filteredHandles);
        SelectedHandle = Handles.FirstOrDefault(x => x.HandleNumber == selectedHandleNumber) ?? Handles.FirstOrDefault();
    }

    private void ApplyLogicalPageFilter(ushort? selectedPageNumber) {
        if (SelectedHandle is null || !_ems.EmmHandles.TryGetValue(SelectedHandle.HandleNumber, out EmmHandle? emmHandle)) {
            LogicalPages = new AvaloniaList<EmsLogicalPageEntryViewModel>();
            SelectedLogicalPage = null;
            RefreshSelectedPageDocument();
            return;
        }

        IEnumerable<EmsLogicalPageEntryViewModel> logicalPages = emmHandle.LogicalPages
            .Select((page, index) => new EmsLogicalPageEntryViewModel(emmHandle.HandleNumber, index, page.PageNumber));

        string trimmedSearch = LogicalPageSearchText.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedSearch)) {
            logicalPages = logicalPages.Where(x => LogicalPageMatchesSearch(x, trimmedSearch));
        }

        LogicalPages = new AvaloniaList<EmsLogicalPageEntryViewModel>(logicalPages);

        SelectedLogicalPage = LogicalPages.FirstOrDefault(x => x.PageNumber == selectedPageNumber) ?? LogicalPages.FirstOrDefault();
    }

    private void RefreshSelectedPageDocument() {
        if (SelectedLogicalPage is null ||
            !_ems.EmmHandles.TryGetValue(SelectedLogicalPage.HandleNumber, out EmmHandle? emmHandle) ||
            SelectedLogicalPage.LogicalPageIndex < 0 ||
            SelectedLogicalPage.LogicalPageIndex >= emmHandle.LogicalPages.Count) {
            SelectedPageDocument = null;
            SelectedPageTitle = "Select an EMS logical page";
            ResetSearchResult();
            return;
        }

        EmmPage selectedPage = emmHandle.LogicalPages[SelectedLogicalPage.LogicalPageIndex];
        SelectedPageDocument = new EmmPageBinaryDocument(selectedPage);
        SelectedPageTitle = $"Handle {SelectedLogicalPage.HandleNumber} - Logical {SelectedLogicalPage.LogicalPageIndex} - Page {ConvertUtils.ToHex16(selectedPage.PageNumber)}";
        ResetSearchResult();
    }

    [RelayCommand(FlowExceptionsToTaskScheduler = true, IncludeCancelCommand = true)]
    private async Task SearchSelectedPage(CancellationToken token) {
        if (SelectedPageDocument is null || string.IsNullOrWhiteSpace(MemorySearchValue) || token.IsCancellationRequested) {
            return;
        }

        byte[]? searchBytes = ParseSearchBytes();
        if (searchBytes is null || searchBytes.Length == 0) {
            return;
        }

        byte[] pageBytes = ReadSelectedPageBytes();
        if (pageBytes.Length == 0 || pageBytes.Length < searchBytes.Length) {
            AddressOFoundOccurence = null;
            FoundOccurrenceDisplay = "Not found";
            IsAddressOfFoundOccurrenceValid = false;
            return;
        }

        try {
            IsBusy = true;
            int? found = await Task.Run(() => FindOccurrence(pageBytes, searchBytes, token), token);
            if (found is null) {
                AddressOFoundOccurence = null;
                FoundOccurrenceDisplay = "Not found";
                IsAddressOfFoundOccurrenceValid = false;
                return;
            }

            AddressOFoundOccurence = (uint)found.Value;
            FoundOccurrenceDisplay = ConvertUtils.ToHex32((uint)found.Value);
            IsAddressOfFoundOccurrenceValid = true;
        } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            return;
        } finally {
            IsBusy = false;
        }
    }

    private EmsPhysicalPageEntryViewModel CreatePhysicalPageEntry(ushort physicalPageNumber, EmmRegister emmRegister) {
        int? owningHandleNumber = FindOwningHandleNumber(emmRegister.PhysicalPage);
        string handleNumber = owningHandleNumber is null ? "Unmapped" : ConvertUtils.ToHex16((ushort)owningHandleNumber.Value);
        string logicalPageNumber = emmRegister.PhysicalPage.PageNumber == ExpandedMemoryManager.EmmNullPage
            ? "Unmapped"
            : ConvertUtils.ToHex16(emmRegister.PhysicalPage.PageNumber);
        SegmentedAddress pageAddress = new(ExpandedMemoryManager.EmmPageFrameSegment,
            (ushort)(physicalPageNumber * ExpandedMemoryManager.EmmPageSize));
        return new EmsPhysicalPageEntryViewModel(
            physicalPageNumber,
            pageAddress.ToString(),
            handleNumber,
            logicalPageNumber);
    }

    private int? FindOwningHandleNumber(EmmPage emmPage) {
        KeyValuePair<int, EmmHandle> owningHandle = _ems.EmmHandles
            .Where(handle => handle.Value.LogicalPages.Any(page => ReferenceEquals(page, emmPage)))
            .FirstOrDefault();
        if (owningHandle.Value is null) {
            return null;
        }
        return owningHandle.Key;
    }

    private static bool HandleMatchesSearch(EmsHandleEntryViewModel handle, string search) {
        string loweredSearch = search.ToLowerInvariant();
        return handle.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            handle.HandleNumber.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
            ConvertUtils.ToHex16(handle.HandleNumber).ToLowerInvariant().Contains(loweredSearch);
    }

    private static bool LogicalPageMatchesSearch(EmsLogicalPageEntryViewModel logicalPage, string search) {
        string loweredSearch = search.ToLowerInvariant();
        return logicalPage.LogicalPageIndex.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
            logicalPage.PageNumber.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
            ConvertUtils.ToHex16(logicalPage.PageNumber).ToLowerInvariant().Contains(loweredSearch);
    }

    private void ResetSearchResult() {
        AddressOFoundOccurence = null;
        FoundOccurrenceDisplay = "-";
        IsAddressOfFoundOccurrenceValid = false;
    }

    private byte[] ReadSelectedPageBytes() {
        if (SelectedPageDocument is null || SelectedPageDocument.Length == 0 || SelectedPageDocument.Length > int.MaxValue) {
            return [];
        }

        byte[] buffer = new byte[(int)SelectedPageDocument.Length];
        SelectedPageDocument.ReadBytes(0, buffer);
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
