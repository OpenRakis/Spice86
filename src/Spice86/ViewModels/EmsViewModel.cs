namespace Spice86.ViewModels;

using Avalonia.Collections;

using AvaloniaHex.Document;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;
using Spice86.ViewModels.DataModels;

public partial class EmsViewModel : ViewModelBase, IEmulatorObjectViewModel {
    private readonly ExpandedMemoryManager _ems;
    private List<EmsHandleEntryViewModel> _allHandles = new();
    private List<EmsPhysicalPageEntryViewModel> _allPhysicalPages = new();

    public EmsViewModel(ExpandedMemoryManager ems) {
        _ems = ems;
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

    partial void OnHandleSearchTextChanged(string value) {
        ushort? selectedHandleNumber = SelectedHandle?.HandleNumber;
        ApplyHandleFilter(selectedHandleNumber);
    }

    partial void OnLogicalPageSearchTextChanged(string value) {
        ushort? selectedPageNumber = SelectedLogicalPage?.PageNumber;
        ApplyLogicalPageFilter(selectedPageNumber);
    }

    public void UpdateValues(object? sender, EventArgs e) {
        if (!IsVisible) {
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
            return;
        }

        EmmPage selectedPage = emmHandle.LogicalPages[SelectedLogicalPage.LogicalPageIndex];
        SelectedPageDocument = new EmmPageBinaryDocument(selectedPage);
        SelectedPageTitle = $"Handle {SelectedLogicalPage.HandleNumber} - Logical {SelectedLogicalPage.LogicalPageIndex} - Page {ConvertUtils.ToHex16(selectedPage.PageNumber)}";
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
        foreach (KeyValuePair<int, EmmHandle> handle in _ems.EmmHandles) {
            if (handle.Value.LogicalPages.Any(page => ReferenceEquals(page, emmPage))) {
                return handle.Key;
            }
        }
        return null;
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
}
