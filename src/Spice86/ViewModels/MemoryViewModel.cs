namespace Spice86.ViewModels;

using Avalonia.Threading;

using AvaloniaHex.Document;
using AvaloniaHex.Editing;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

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

    public MemoryViewModel(IMemory memory, BreakpointsViewModel breakpointsViewModel, IPauseHandler pauseHandler, IMessenger messenger, IUIDispatcher uiDispatcher,
        ITextClipboard textClipboard, IHostStorageProvider storageProvider, IStructureViewModelFactory structureViewModelFactory,
        bool canCloseTab = false, uint startAddress = 0, uint endAddress = A20Gate.EndOfHighMemoryArea) : base(uiDispatcher, textClipboard) {
        _pauseHandler = pauseHandler;
        _breakpointsViewModel = breakpointsViewModel;
        _memory = memory;
        _pauseHandler.Pausing += OnPause;
        IsPaused = pauseHandler.IsPaused;
        pauseHandler.Resumed += () => _uiDispatcher.Post(() => IsPaused = false);
        _messenger = messenger;
        _storageProvider = storageProvider;
        _structureViewModelFactory = structureViewModelFactory;
        StartAddress = startAddress;
        EndAddress = endAddress;
        CanCloseTab = canCloseTab;
        if (EndAddress is 0) {
            EndAddress = _memory.Length;
        }
        IsMemoryRangeValid = GetIsMemoryRangeValid();
        TryUpdateHeaderAndMemoryDocument();
    }

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

    private uint? _startAddress = 0;

    [NotifyCanExecuteChangedFor(nameof(UpdateBinaryDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DumpMemoryCommand))]
    [ObservableProperty]
    private bool _isMemoryRangeValid;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    private bool _canCloseTab;

    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab() => _messenger.Send(new RemoveViewModelMessage<MemoryViewModel>(this));

    private bool GetIsMemoryRangeValid() =>
        StartAddress <= (EndAddress ?? _memory.Length)
        && EndAddress >= (StartAddress ?? 0);

    public uint? StartAddress {
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

    private uint? _endAddress = 0;

    public uint? EndAddress {
        get => _endAddress;
        set {
            SetProperty(ref _endAddress, value);
            IsMemoryRangeValid = GetIsMemoryRangeValid();
            TryUpdateHeaderAndMemoryDocument();
        }
    }

    [ObservableProperty]
    private bool _creatingMemoryBreakpoint;

    private bool IsSelectionRangeValid() => SelectionRange is not null && StartAddress is not null;

    [RelayCommand(CanExecute = nameof(IsSelectionRangeValid))]
    private void BeginCreateMemoryBreakpoint() {
        CreatingMemoryBreakpoint = true;
        if (StartAddress is not null && SelectionRange is not null) {
            ulong rangeStart = StartAddress.Value + SelectionRange.Value.Start.ByteIndex;
            ulong rangEnd = StartAddress.Value + SelectionRange.Value.End.ByteIndex;
            BreakpointRangeStartAddress = rangeStart.ToString(CultureInfo.InvariantCulture);
            if (rangeStart != rangEnd) {
                BreakpointRangeEndAddress = rangEnd.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCreateMemoryBreakpointCommand))]
    private string? _breakpointRangeEndAddress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCreateMemoryBreakpointCommand))]
    private string? _breakpointRangeStartAddress;

    [ObservableProperty]
    private BreakPointType _selectedBreakpointType = BreakPointType.ACCESS;

    public BreakPointType[] BreakpointTypes => [BreakPointType.ACCESS, BreakPointType.WRITE, BreakPointType.READ];

    private void OnBreakPointReached() {
        string message = $"Memory breakpoint was reached.";
        _pauseHandler.RequestPause(message);
        _uiDispatcher.Post(() => {
            _messenger.Send(new StatusMessage(DateTime.Now, this, message));
            TryUpdateHeaderAndMemoryDocument();
        });
    }

    private bool IsBreakpointRangeValid() {
        bool isValid = TryParseMemoryAddress(BreakpointRangeStartAddress, out _);
        if(!string.IsNullOrWhiteSpace(BreakpointRangeEndAddress)) {
            isValid = TryParseMemoryAddress(BreakpointRangeStartAddress, out _);
        }
        return isValid;
    }

    [RelayCommand(CanExecute = nameof(IsBreakpointRangeValid))]
    private void ConfirmCreateMemoryBreakpoint() {
        if (TryParseMemoryAddress(BreakpointRangeStartAddress, out ulong? breakpointStartAddressValue)
            && TryParseMemoryAddress(BreakpointRangeEndAddress, out ulong? endAddressValue)) {
            for (ulong i = breakpointStartAddressValue.Value; i <= endAddressValue.Value; i++) {
                CreateMemoryAddressBreakpoint(i);
            }
        }
        else if (TryParseMemoryAddress(BreakpointRangeStartAddress, out ulong? breakPointAddress)) {
            CreateMemoryAddressBreakpoint(breakPointAddress.Value);
        }
        CreatingMemoryBreakpoint = false;
    }

    private void CreateMemoryAddressBreakpoint(ulong breakpointAddressValue) {
        _breakpointsViewModel.AddAddressBreakpoint(
            (uint)breakpointAddressValue,
            SelectedBreakpointType,
            isRemovedOnTrigger: false,
            OnBreakPointReached, 
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
            await _textClipboard.SetTextAsync($"{hexRepresentation}").ConfigureAwait(false);
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
            await SearchMemoryCommand.ExecuteAsync(null).ConfigureAwait(false);
        }
    }
    
    [RelayCommand]
    private async Task NextOccurrence() {
        if (SearchMemoryCommand.CanExecute(null)) {
            _searchDirection = SearchDirection.Forward;
            await SearchMemoryCommand.ExecuteAsync(null).ConfigureAwait(false);
        }
    }
    
    [RelayCommand]
    private async Task FirstOccurrence() {
        if (SearchMemoryCommand.CanExecute(null)) {
            _searchDirection = SearchDirection.FirstOccurence;
            await SearchMemoryCommand.ExecuteAsync(null).ConfigureAwait(false);
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
            uint searchStartAddress =  0;
            
            if (_searchDirection == SearchDirection.Forward && AddressOFoundOccurence is not null) {
                searchStartAddress = AddressOFoundOccurence.Value + 1;
                searchLength = (int)(_memory.Length - searchStartAddress);
            } else if (_searchDirection == SearchDirection.Backward && AddressOFoundOccurence is not null) {
                searchStartAddress = 0;
                searchLength = (int)(AddressOFoundOccurence.Value - 1);
            }
            if(SearchDataType == MemorySearchDataType.Binary && ConvertUtils.TryParseHexToByteArray(MemorySearchValue, out byte[]? searchBytes)) {
                AddressOFoundOccurence = await PerformMemorySearchAsync(searchStartAddress, searchLength, searchBytes, token).ConfigureAwait(false);
            } else if(SearchDataType == MemorySearchDataType.Ascii) {
                searchBytes = Encoding.ASCII.GetBytes(MemorySearchValue);
                AddressOFoundOccurence = await PerformMemorySearchAsync(searchStartAddress, searchLength, searchBytes, token).ConfigureAwait(false);
            }
        } finally {
            await _uiDispatcher.InvokeAsync(() => {
                IsBusy = false;
                IsAddressOfFoundOccurrenceValid = AddressOFoundOccurence is not null;
            });
        }
    }

    private async Task<uint?> PerformMemorySearchAsync(uint searchStartAddress, int searchLength, byte[] searchBytes, CancellationToken token) =>
        await Task.Run(
            () =>_memory.SearchValue(searchStartAddress, searchLength, searchBytes), token).ConfigureAwait(false);

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
        }
    }

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

    private void CreateNewMemoryView(uint? startAddress = null) {
        MemoryViewModel memoryViewModel = new(_memory, _breakpointsViewModel, _pauseHandler, _messenger, _uiDispatcher, _textClipboard,
            _storageProvider,
            _structureViewModelFactory, canCloseTab: true);
        if (startAddress is not null) {
            memoryViewModel.StartAddress = startAddress;
        }
        _messenger.Send(new AddViewModelMessage<MemoryViewModel>(memoryViewModel));
    }

    [RelayCommand(CanExecute = nameof(IsMemoryRangeValid))]
    private void UpdateBinaryDocument() {
        if (StartAddress is null || EndAddress is null) {
            return;
        }
        DataMemoryDocument = new DataMemoryDocument(_memory, StartAddress.Value, EndAddress.Value);
        DataMemoryDocument.MemoryReadInvalidOperation += OnMemoryReadInvalidOperation;
    }

    private void OnMemoryReadInvalidOperation(Exception exception) {
        Dispatcher.UIThread.Post(() => ShowError(exception));
    }

    [RelayCommand(CanExecute = nameof(IsMemoryRangeValid))]
    private async Task DumpMemory() {
        if (StartAddress is not null && EndAddress is not null) {
            await _storageProvider.SaveBinaryFile(_memory.ReadRam(EndAddress.Value - StartAddress.Value, StartAddress.Value));
        }
    }

    [ObservableProperty]
    private bool _isEditingMemory;

    [ObservableProperty]
    private string? _memoryEditAddress;

    [ObservableProperty]
    private string? _memoryEditValue = "";

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void EditMemory() {
        IsEditingMemory = true;
        try {
        if (MemoryEditAddress is not null && TryParseMemoryAddress(MemoryEditAddress, out ulong? memoryEditAddressValue)) {
            MemoryEditValue = Convert.ToHexString(_memory.ReadRam((uint)(MemoryEditValue?.Length ?? sizeof(ushort)), (uint)memoryEditAddressValue.Value));
        }
        } catch (Exception e) {
            ShowError(e);
        }
    }

    [RelayCommand]
    private void CancelMemoryEdit() => IsEditingMemory = false;

    [RelayCommand]
    private void ApplyMemoryEdit() {
        if (!TryParseMemoryAddress(MemoryEditAddress, out ulong? address) ||
            MemoryEditValue is null ||
            !long.TryParse(MemoryEditValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value)) {
            return;
        }
        try {
            DataMemoryDocument?.WriteBytes(address!.Value, BitConverter.GetBytes(value));
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