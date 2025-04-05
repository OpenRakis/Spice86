namespace Spice86.ViewModels;

using Avalonia.Threading;

using AvaloniaHex.Document;
using AvaloniaHex.Editing;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Infrastructure;
using Spice86.MemoryWrappers;
using Spice86.Messages;
using Spice86.Shared.Utils;
using Spice86.Views;

using System.Globalization;
using System.Text;

public partial class MemoryViewModel : ViewModelWithErrorDialog {
    private readonly IMemory _memory;
    private readonly IStructureViewModelFactory _structureViewModelFactory;
    private readonly IMessenger _messenger;
    private readonly IPauseHandler _pauseHandler;
    private readonly BreakpointsViewModel _breakpointsViewModel;
    private readonly MemoryDataExporter _memoryDataExporter;
    private readonly State _state;

    public MemoryViewModel(IMemory memory, MemoryDataExporter memoryDataExporter,
        State state, BreakpointsViewModel breakpointsViewModel,
        IPauseHandler pauseHandler, IMessenger messenger, IUIDispatcher uiDispatcher,
        ITextClipboard textClipboard, IHostStorageProvider storageProvider,
        IStructureViewModelFactory structureViewModelFactory,
        bool canCloseTab = false, string? startAddress = null, string? endAddress = null)
        : base(uiDispatcher, textClipboard) {
        _state = state;
        _pauseHandler = pauseHandler;
        _memoryDataExporter = memoryDataExporter;
        _breakpointsViewModel = breakpointsViewModel;
        _memory = memory;
        _pauseHandler.Paused += OnPaused;
        IsPaused = pauseHandler.IsPaused;
        pauseHandler.Resumed += () => _uiDispatcher.Post(() => IsPaused = false);
        _messenger = messenger;
        _storageProvider = storageProvider;
        _structureViewModelFactory = structureViewModelFactory;
        if (TryParseAddressString(startAddress, _state, out uint? startAddressValue)) {
            StartAddress = ConvertUtils.ToHex32(startAddressValue.Value);
        } else {
            StartAddress = "0x0";
        }
        if (TryParseAddressString(endAddress, _state, out uint? endAddressValue)) {
            EndAddress = ConvertUtils.ToHex32(endAddressValue.Value);
        } else {
            EndAddress = ConvertUtils.ToHex32(_memory.Length);
        }
        CanCloseTab = canCloseTab;
        IsMemoryRangeValid = GetIsMemoryRangeValid(
            startAddressValue.HasValue ? startAddressValue.Value : 0,
            endAddressValue.HasValue ? endAddressValue.Value : _memory.Length);
        TryUpdateHeaderAndMemoryDocument();
    }
    public State State => _state;

    [ObservableProperty]
    private string? _title;

    public enum MemorySearchDataType {
        Binary,
        Ascii,
    }

    [ObservableProperty]
    private MemorySearchDataType _searchDataType;

    public bool SearchDataTypeIsBinary => SearchDataType == MemorySearchDataType.Binary;

    public bool SearchDataTypeIsAscii => SearchDataType == MemorySearchDataType.Ascii;

    [RelayCommand]
    private void SetSearchDataTypeToBinary() => SetSearchDataType(MemorySearchDataType.Binary);

    private void SetSearchDataType(MemorySearchDataType searchDataType) {
        SearchDataType = searchDataType;
        OnPropertyChanged(nameof(SearchDataTypeIsBinary));
        OnPropertyChanged(nameof(SearchDataTypeIsAscii));
    }

    [RelayCommand]
    private void SetSearchDataTypeToAscii() => SetSearchDataType(MemorySearchDataType.Ascii);

    [ObservableProperty]
    private DataMemoryDocument? _dataMemoryDocument;

    private string? _startAddress = "0x0";

    [NotifyCanExecuteChangedFor(nameof(UpdateBinaryDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DumpMemoryCommand))]
    [ObservableProperty]
    private bool _isMemoryRangeValid;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    private bool _canCloseTab;

    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab() => _messenger.Send(new RemoveViewModelMessage<MemoryViewModel>(this));

    public string? StartAddress {
        get => _startAddress;
        set {
            if (ValidateAddressRange(_state,value, EndAddress,
                nameof(StartAddress))) {
                IsMemoryRangeValid = true;
                if (SetProperty(ref _startAddress, value)) {
                    TryUpdateHeaderAndMemoryDocument();
                }
            } else {
                IsMemoryRangeValid = false;
                DataMemoryDocument = null;
            }
        }
    }

    private void TryUpdateHeaderAndMemoryDocument() {
        Header = $"{StartAddress:X} - {EndAddress:X}";
        if (UpdateBinaryDocumentCommand.CanExecute(null)) {
            UpdateBinaryDocumentCommand.Execute(null);
        }
    }

    private string? _endAddress = "0x0";

    public string? EndAddress {
        get => _endAddress;
        set {
            if (ValidateAddressRange(_state, StartAddress, value,
                nameof(EndAddress))) {
                IsMemoryRangeValid = true;
                if (SetProperty(ref _endAddress, value)) {
                    TryUpdateHeaderAndMemoryDocument();
                }
            } else {
                IsMemoryRangeValid = false;
                DataMemoryDocument = null;
            }
        }
    }

    [ObservableProperty]
    private bool _creatingMemoryBreakpoint;

    private bool IsSelectionRangeValid() => SelectionRange is not null && StartAddress is not null;

    [RelayCommand(CanExecute = nameof(IsSelectionRangeValid))]
    private void BeginCreateMemoryBreakpoint() {
        CreatingMemoryBreakpoint = true;
        if (TryParseAddressString(StartAddress, _state,
            out uint? startAddress) && SelectionRange is not null) {
            uint rangeStart = (uint)(startAddress.Value + SelectionRange.Value.Start.ByteIndex);
            uint rangeEnd = (uint)(startAddress.Value + SelectionRange.Value.End.ByteIndex);
            MemoryBreakpointStartAddress = ConvertUtils.ToHex32(rangeStart);
            if (rangeStart != rangeEnd) {
                MemoryBreakpointEndAddress = ConvertUtils.ToHex32(rangeEnd);
            }
        }
    }

    private string? _memoryBreakpointEndAddress;

    public string? MemoryBreakpointEndAddress {
        get => _memoryBreakpointEndAddress;
        set {
            if (SetProperty(ref _memoryBreakpointEndAddress, value)) {
                ConfirmCreateMemoryBreakpointCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? MemoryBreakpointStartAddress {
        get => _memoryBreakpointStartAddress;
        set {
            if (ValidateAddressProperty(value, _state) &&
                SetProperty(ref _memoryBreakpointStartAddress, value)) {
                ConfirmCreateMemoryBreakpointCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string? _memoryBreakpointStartAddress;

    [ObservableProperty]
    private BreakPointType _selectedBreakpointType = BreakPointType.MEMORY_ACCESS;

    public BreakPointType[] BreakpointTypes => [BreakPointType.MEMORY_ACCESS, BreakPointType.MEMORY_WRITE, BreakPointType.MEMORY_READ];

    private bool ConfirmCreateMemoryBreakpointCanExecute() {
        return
            TryParseAddressString(MemoryBreakpointStartAddress, _state, out uint? _) &&
            TryParseAddressString(MemoryBreakpointEndAddress, _state, out uint? _);
    }


    [RelayCommand(CanExecute = nameof(ConfirmCreateMemoryBreakpointCanExecute))]
    private void ConfirmCreateMemoryBreakpoint() {
        if (TryParseAddressString(MemoryBreakpointStartAddress, _state, out uint? breakpointRangeStartAddress) &&
            TryParseAddressString(MemoryBreakpointEndAddress, _state, out uint? breakpointRangeEndAddress)) {
                _breakpointsViewModel.CreateMemoryBreakpointRangeAtAddresses(
                    breakpointRangeStartAddress.Value,
                    breakpointRangeEndAddress.Value);
        } else if (breakpointRangeStartAddress != null) {
            _breakpointsViewModel.CreateMemoryBreakpointAtAddress(breakpointRangeStartAddress.Value);
        }
        CreatingMemoryBreakpoint = false;
    }

    [RelayCommand]
    private void CancelCreateMemoryBreakpoint() => CreatingMemoryBreakpoint = false;

    [RelayCommand(CanExecute = nameof(IsSelectionRangeValid))]
    public async Task CopySelection() {
        if (SelectionRange is not null &&
            TryParseAddressString(StartAddress, _state, out uint? address)) {
            byte[] memoryBytes = _memory.ReadRam(
                (uint)(address.Value + SelectionRange.Value.Start.ByteIndex),
                (uint)SelectionRange.Value.ByteLength);
            string hexRepresentation = ConvertUtils.ByteArrayToHexString(memoryBytes);
            await _textClipboard.SetTextAsync($"{hexRepresentation}");
        }
    }

    [ObservableProperty]
    private string? _memorySearchValue;

    private enum SearchDirection {
        FirstOccurence,
        Forward,
        Backward,
    }

    private SearchDirection _searchDirection;

    [RelayCommand]
    private async Task PreviousOccurrence() {
        if (SearchMemoryCommand.CanExecute(null)) {
            _searchDirection = SearchDirection.Backward;
            await SearchMemoryCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task NextOccurrence() {
        if (SearchMemoryCommand.CanExecute(null)) {
            _searchDirection = SearchDirection.Forward;
            await SearchMemoryCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task FirstOccurrence() {
        if (SearchMemoryCommand.CanExecute(null)) {
            _searchDirection = SearchDirection.FirstOccurence;
            await SearchMemoryCommand.ExecuteAsync(null);
        }
    }

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private void StartMemorySearch() {
        IsSearchingMemory = true;
    }

    [ObservableProperty]
    private bool _isSearchingMemory;

    [ObservableProperty]
    private uint? _addressOFoundOccurence;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoToFoundOccurenceCommand))]
    private bool _isAddressOfFoundOccurrenceValid;

    [RelayCommand(CanExecute = nameof(IsPaused), FlowExceptionsToTaskScheduler = false, IncludeCancelCommand = true)]
    private async Task SearchMemory(CancellationToken token) {
        if (string.IsNullOrWhiteSpace(MemorySearchValue) || token.IsCancellationRequested) {
            return;
        }
        try {
            IsBusy = true;
            int searchLength = (int)_memory.Length;
            uint searchStartAddress = 0;

            if (_searchDirection == SearchDirection.Forward && AddressOFoundOccurence is not null) {
                searchStartAddress = AddressOFoundOccurence.Value + 1;
                searchLength = (int)(_memory.Length - searchStartAddress);
            } else if (_searchDirection == SearchDirection.Backward && AddressOFoundOccurence is not null) {
                searchStartAddress = 0;
                searchLength = (int)(AddressOFoundOccurence.Value - 1);
            }
            if (SearchDataType == MemorySearchDataType.Binary && ConvertUtils.TryParseHexToByteArray(MemorySearchValue, out byte[]? searchBytes)) {
                AddressOFoundOccurence = await PerformMemorySearchAsync(searchStartAddress, searchLength, searchBytes, token);
            } else if (SearchDataType == MemorySearchDataType.Ascii) {
                searchBytes = Encoding.ASCII.GetBytes(MemorySearchValue);
                AddressOFoundOccurence = await PerformMemorySearchAsync(searchStartAddress, searchLength, searchBytes, token);
            }
        } finally {
            await _uiDispatcher.InvokeAsync(() => {
                IsBusy = false;
                IsAddressOfFoundOccurrenceValid = AddressOFoundOccurence is not null;
            });
        }
    }

    private async Task<uint?> PerformMemorySearchAsync(uint searchStartAddress,
        int searchLength, byte[] searchBytes, CancellationToken token) {
        return await Task.Run(
            () => {
                uint? value = _memory.SearchValue(searchStartAddress, searchLength, searchBytes);
                return value;
            }, token);
    }

    [RelayCommand(CanExecute = nameof(IsAddressOfFoundOccurrenceValid))]
    private void GoToFoundOccurence() {
        if (AddressOFoundOccurence is not null &&
            NewMemoryViewCommand.CanExecute(null)) {
            CreateNewMemoryView(AddressOFoundOccurence.Value);
        }
    }

    [ObservableProperty]
    private string _header = "Memory View";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewMemoryViewCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditMemoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchMemoryCommand))]
    private bool _isPaused;

    [ObservableProperty]
    private BitRange? _selectionRange;

    public bool IsStructureInfoPresent => _structureViewModelFactory.IsInitialized;

    private readonly IHostStorageProvider _storageProvider;

    /// <summary>
    /// Handles the event when the selection range within the HexEditor changes.
    /// </summary>
    /// <param name="sender">The source of the event, expected to be of type <see cref="Selection"/>.</param>
    /// <param name="e">The event arguments, not used in this method.</param>
    public void OnSelectionRangeChanged(object? sender, EventArgs e) {
        Selection? selection = (sender as Selection);
        if (selection != null &&
            TryParseAddressString(StartAddress, _state, out uint? startAddress) &&
            TryParseAddressString(EndAddress, _state, out uint? endAddress)) {
            SelectionRange = selection.Range;
            SelectionRangeStartAddress = ConvertUtils.ToHex32(
                (uint)(startAddress.Value + selection.Range.Start.ByteIndex));
            SelectionRangeEndAddress = ConvertUtils.ToHex32(
                (uint)(endAddress.Value + selection.Range.End.ByteIndex));
        }
    }

    [ObservableProperty]
    private string? _selectionRangeStartAddress;

    [ObservableProperty]
    private string? _selectionRangeEndAddress;

    [RelayCommand(CanExecute = nameof(IsStructureInfoPresent))]
    public void ShowStructureView() {
        if (DataMemoryDocument == null) {
            return;
        }

        // Use either the selected range or the entire document if no range is selected.
        IBinaryDocument data;
        if (SelectionRange is { ByteLength: > 1 } bitRange) {
            byte[] bytes = new byte[bitRange.ByteLength];
            DataMemoryDocument.ReadBytes(bitRange.Start.ByteIndex, bytes);
            data = new MemoryBinaryDocument(bytes);
        } else {
            data = DataMemoryDocument;
        }
        StructureViewModel structureViewModel = _structureViewModelFactory.CreateNew(data);
        var structureWindow = new StructureView { DataContext = structureViewModel };
        structureWindow.Show();
    }

    private void OnPaused() {
        _uiDispatcher.Post(() => {
            IsPaused = true;
            UpdateBinaryDocument();
        });
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void NewMemoryView() {
        CreateNewMemoryView();
    }

    private void CreateNewMemoryView(uint? startAddress = null) {
        MemoryViewModel memoryViewModel = new(_memory, _memoryDataExporter, _state,
            _breakpointsViewModel, _pauseHandler,
            _messenger, _uiDispatcher, _textClipboard,
            _storageProvider, _structureViewModelFactory, canCloseTab: true);
        if (startAddress is not null) {
            memoryViewModel.StartAddress = ConvertUtils.ToHex32(startAddress.Value);
        }
        _messenger.Send(new AddViewModelMessage<MemoryViewModel>(memoryViewModel));
    }

    [RelayCommand(CanExecute = nameof(IsMemoryRangeValid))]
    private void UpdateBinaryDocument() {
        if (!TryParseAddressString(StartAddress, _state, out uint? startAddress) ||
            !TryParseAddressString(EndAddress, _state, out uint? endAddress) ||
            !IsMemoryRangeValid) {
            return;
        }
        DataMemoryDocument = new DataMemoryDocument(_memory,
            startAddress.Value, endAddress.Value);
        DataMemoryDocument.MemoryReadInvalidOperation += OnMemoryReadInvalidOperation;
    }

    private void OnMemoryReadInvalidOperation(Exception exception) {
        Dispatcher.UIThread.Post(() => ShowError(exception));
    }

    [RelayCommand(CanExecute = nameof(IsMemoryRangeValid))]
    private async Task DumpMemory() {
        await _storageProvider.SaveBinaryFile(_memoryDataExporter.GenerateToolingCompliantRamDump());
    }

    [ObservableProperty]
    private bool _isEditingMemory;

    private string? _memoryEditAddress;

    public string? MemoryEditAddress {
        get => _memoryEditAddress;
        set {
            if (ValidateAddressProperty(value, _state)) {
                SetProperty(ref _memoryEditAddress, value);
            }
        }
    }

    [ObservableProperty]
    private string? _memoryEditValue = "";

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void EditMemory() {
        IsEditingMemory = true;
        try {
            if (TryParseAddressString(MemoryEditAddress, _state, out uint? address)) {
                MemoryEditValue = Convert.ToHexString(_memory.ReadRam(
                    (uint)(MemoryEditValue?.Length ?? sizeof(ushort)),
                        address.Value));
            }
        } catch (Exception e) {
            ShowError(e);
        }
    }

    [RelayCommand]
    private void CancelMemoryEdit() => IsEditingMemory = false;

    [RelayCommand]
    private void ApplyMemoryEdit() {
        if (MemoryEditValue is null ||
            !long.TryParse(MemoryEditValue, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out long value)) {
            return;
        }
        try {
            if (TryParseAddressString(MemoryEditAddress, _state, out uint? address)) {
                DataMemoryDocument?.WriteBytes(address.Value, BitConverter.GetBytes(value));
            }
        } catch (IndexOutOfRangeException e) {
            ShowError(e);
            MemoryEditValue = null;
            MemoryEditAddress = null;
        } finally {
            IsEditingMemory = false;
        }
    }
}