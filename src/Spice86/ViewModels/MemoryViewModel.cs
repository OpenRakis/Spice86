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
using Spice86.Models.Debugging;
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
    private readonly State _state;
    private readonly MemoryDataExporter _memoryDataExporter;

    public MemoryViewModel(IMemory memory, MemoryDataExporter memoryDataExporter, State state, BreakpointsViewModel breakpointsViewModel, IPauseHandler pauseHandler, IMessenger messenger, IUIDispatcher uiDispatcher,
        ITextClipboard textClipboard, IHostStorageProvider storageProvider, IStructureViewModelFactory structureViewModelFactory,
        bool canCloseTab = false, LinearMemoryAddress? startAddress = null, LinearMemoryAddress? endAddress = null) : base(uiDispatcher, textClipboard) {
        _pauseHandler = pauseHandler;
        _memoryDataExporter = memoryDataExporter;
        _state = state;
        _breakpointsViewModel = breakpointsViewModel;
        _memory = memory;
        _pauseHandler.Paused += OnPause;
        IsPaused = pauseHandler.IsPaused;
        pauseHandler.Resumed += () => _uiDispatcher.Post(() => IsPaused = false);
        _messenger = messenger;
        _storageProvider = storageProvider;
        _structureViewModelFactory = structureViewModelFactory;
        StartAddress = startAddress ?? 0;
        EndAddress = endAddress ?? _memory.Length;
        CanCloseTab = canCloseTab;
        IsMemoryRangeValid = GetIsMemoryRangeValid();
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

    private LinearMemoryAddress? _startAddress = new(0);

    [NotifyCanExecuteChangedFor(nameof(UpdateBinaryDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DumpMemoryCommand))]
    [ObservableProperty]
    private bool _isMemoryRangeValid;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    private bool _canCloseTab;

    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab() => _messenger.Send(new RemoveViewModelMessage<MemoryViewModel>(this));

    private bool GetIsMemoryRangeValid() {
        return StartAddress?.Address<= (EndAddress ?? _memory.Length)
        && EndAddress?.Address >= (StartAddress ?? 0) &&
        StartAddress?.Address != EndAddress;
    }

    public LinearMemoryAddress? StartAddress {
        get => _startAddress;
        set {
            SetProperty(ref _startAddress, value);
            IsMemoryRangeValid = GetIsMemoryRangeValid();
            TryUpdateHeaderAndMemoryDocument();
        }
    }

    private void TryUpdateHeaderAndMemoryDocument() {
        Header = $"{StartAddress:X} - {EndAddress:X}";
        if (UpdateBinaryDocumentCommand.CanExecute(null)) {
            UpdateBinaryDocumentCommand.Execute(null);
        }
    }

    private LinearMemoryAddress? _endAddress = new(0);

    public LinearMemoryAddress? EndAddress {
        get => _endAddress;
        set {
            SetProperty(ref _endAddress, value);
            IsMemoryRangeValid = GetIsMemoryRangeValid();
            if (IsMemoryRangeValid) {
                TryUpdateHeaderAndMemoryDocument();
            } else {
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
        if (StartAddress is not null && SelectionRange is not null) {
            uint rangeStart = (uint)(StartAddress.Value + SelectionRange.Value.Start.ByteIndex);
            uint rangEnd = (uint)(StartAddress.Value + SelectionRange.Value.End.ByteIndex);
            BreakpointRangeStartAddress = rangeStart;
            if (rangeStart != rangEnd) {
                BreakpointRangeEndAddress = rangEnd;
            }
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCreateMemoryBreakpointCommand))]
    private LinearMemoryAddress? _breakpointRangeEndAddress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCreateMemoryBreakpointCommand))]
    private LinearMemoryAddress? _breakpointRangeStartAddress;

    [ObservableProperty]
    private BreakPointType _selectedBreakpointType = BreakPointType.MEMORY_ACCESS;

    public BreakPointType[] BreakpointTypes => [BreakPointType.MEMORY_ACCESS, BreakPointType.MEMORY_WRITE, BreakPointType.MEMORY_READ];

    private void OnBreakPointReached(LinearMemoryAddress linearMemoryAddress) {
        string message = $"Memory breakpoint was reached at {linearMemoryAddress}.";
        _pauseHandler.RequestPause(message);
        _uiDispatcher.Post(() => {
            _messenger.Send(new StatusMessage(DateTime.Now, this, message));
            TryUpdateHeaderAndMemoryDocument();
        });
    }


    [RelayCommand]
    private void ConfirmCreateMemoryBreakpoint() {
        if (BreakpointRangeStartAddress != null
            && BreakpointRangeEndAddress != null) {
            for (uint i = BreakpointRangeStartAddress.Value; i <= BreakpointRangeEndAddress.Value; i++) {
                CreateMemoryAddressBreakpoint(i);
            }
        }
        else if (BreakpointRangeStartAddress != null) {
            CreateMemoryAddressBreakpoint(BreakpointRangeStartAddress.Value);
        }
        CreatingMemoryBreakpoint = false;
    }

    private void CreateMemoryAddressBreakpoint(LinearMemoryAddress breakpointAddressValue) {
        _breakpointsViewModel.AddAddressBreakpoint(
            breakpointAddressValue,
            SelectedBreakpointType,
            isRemovedOnTrigger: false,
            () => OnBreakPointReached(breakpointAddressValue), 
            "Memory breakpoint");
    }

    [RelayCommand]
    private void CancelCreateMemoryBreakpoint() => CreatingMemoryBreakpoint = false;

    [RelayCommand(CanExecute = nameof(IsSelectionRangeValid))]
    public async Task CopySelection() {
        if (SelectionRange is not null && StartAddress is not null) {
            byte[] memoryBytes = _memory.ReadRam(
                (uint)(StartAddress.Value + SelectionRange.Value.Start.ByteIndex),
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
    private LinearMemoryAddress? _addressOFoundOccurence;

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
            uint searchStartAddress =  0;
            
            if (_searchDirection == SearchDirection.Forward && AddressOFoundOccurence is not null) {
                searchStartAddress = AddressOFoundOccurence.Value + 1;
                searchLength = (int)(_memory.Length - searchStartAddress);
            } else if (_searchDirection == SearchDirection.Backward && AddressOFoundOccurence is not null) {
                searchStartAddress = 0;
                searchLength = (int)(AddressOFoundOccurence.Value - 1);
            }
            if(SearchDataType == MemorySearchDataType.Binary && ConvertUtils.TryParseHexToByteArray(MemorySearchValue, out byte[]? searchBytes)) {
                AddressOFoundOccurence = await PerformMemorySearchAsync(searchStartAddress, searchLength, searchBytes, token);
            } else if(SearchDataType == MemorySearchDataType.Ascii) {
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

    private async Task<LinearMemoryAddress?> PerformMemorySearchAsync(uint searchStartAddress, int searchLength, byte[] searchBytes, CancellationToken token) {
        return await Task.Run(
            () => {
                uint? value = _memory.SearchValue(searchStartAddress, searchLength, searchBytes);
                return value;
            }, token);
    }

    [RelayCommand(CanExecute = nameof(IsAddressOfFoundOccurrenceValid))]
    private void GoToFoundOccurence() {
        if (AddressOFoundOccurence is not null && NewMemoryViewCommand.CanExecute(null)) {
            CreateNewMemoryView(AddressOFoundOccurence);
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
        if (selection != null) {
            SelectionRange = selection.Range;
            SelectionRangeStartAddress = (uint?)(StartAddress + selection.Range.Start.ByteIndex);
            SelectionRangeEndAddress = (uint?)(StartAddress + selection.Range.End.ByteIndex);
        }
    }

    [ObservableProperty]
    private LinearMemoryAddress? _selectionRangeStartAddress;

    [ObservableProperty]
    private LinearMemoryAddress? _selectionRangeEndAddress;

    [RelayCommand(CanExecute = nameof(IsStructureInfoPresent))]
    public void ShowStructureView() {
        if (DataMemoryDocument == null) {
            return;
        }

        // Use either the selected range or the entire document if no range is selected.
        IBinaryDocument data;
        if (SelectionRange is {ByteLength: > 1} bitRange) {
            byte[] bytes = new byte[bitRange.ByteLength];
            DataMemoryDocument.ReadBytes(bitRange.Start.ByteIndex, bytes);
            data = new MemoryBinaryDocument(bytes);
        } else {
            data = DataMemoryDocument;
        }
        StructureViewModel structureViewModel = _structureViewModelFactory.CreateNew(data);
        var structureWindow = new StructureView {DataContext = structureViewModel};
        structureWindow.Show();
    }
    
    private void OnPause() {
        _uiDispatcher.Post(() => {
            IsPaused = true;
            UpdateBinaryDocument();
        });
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void NewMemoryView() {
        CreateNewMemoryView();
    }

    private void CreateNewMemoryView(LinearMemoryAddress? startAddress = null) {
        MemoryViewModel memoryViewModel = new(_memory, _memoryDataExporter, _state,
            _breakpointsViewModel, _pauseHandler,
            _messenger, _uiDispatcher, _textClipboard,
            _storageProvider, _structureViewModelFactory, canCloseTab: true);
        if (startAddress is not null) {
            memoryViewModel.StartAddress = startAddress;
        }
        _messenger.Send(new AddViewModelMessage<MemoryViewModel>(memoryViewModel));
    }

    [RelayCommand(CanExecute = nameof(IsMemoryRangeValid))]
    private void UpdateBinaryDocument() {
        if (StartAddress is null || EndAddress is null || !IsMemoryRangeValid) {
            return;
        }
        DataMemoryDocument = new DataMemoryDocument(_memory,
            StartAddress.Value, EndAddress.Value);
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

    [ObservableProperty]
    private LinearMemoryAddress? _memoryEditAddress;

    [ObservableProperty]
    private string? _memoryEditValue = "";

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void EditMemory() {
        IsEditingMemory = true;
        try {
        if (MemoryEditAddress is not null) {
            MemoryEditValue = Convert.ToHexString(_memory.ReadRam(
                (uint)(MemoryEditValue?.Length ?? sizeof(ushort)), 
                    (uint)MemoryEditAddress.Value));
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
            !long.TryParse(MemoryEditValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value)) {
            return;
        }
        try {
            DataMemoryDocument?.WriteBytes(MemoryEditAddress!.Value, BitConverter.GetBytes(value));
        } catch (IndexOutOfRangeException e) {
            ShowError(e);
            MemoryEditValue = null;
            MemoryEditAddress = null;
        }
        finally {
            IsEditingMemory = false;
        }
    }
}