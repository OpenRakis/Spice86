namespace Spice86.ViewModels;

using Avalonia.Threading;

using AvaloniaHex.Document;
using AvaloniaHex.Editing;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Infrastructure;
using Spice86.MemoryWrappers;
using Spice86.Messages;
using Spice86.Shared.Utils;
using Spice86.Views;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

public partial class MemoryViewModel : ViewModelWithErrorDialog {
    private readonly IMemory _memory;
    private readonly IStructureViewModelFactory _structureViewModelFactory;
    private readonly IMessenger _messenger;
    private readonly IPauseHandler _pauseHandler;

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
    private string _header = "Memory View";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewMemoryViewCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditMemoryCommand))]
    private bool _isPaused;

    [ObservableProperty]
    private BitRange? _selectionRange;

    public bool IsStructureInfoPresent => _structureViewModelFactory.IsInitialized;

    private readonly IHostStorageProvider _storageProvider;

    public MemoryViewModel(IMemory memory, IPauseHandler pauseHandler, IMessenger messenger, ITextClipboard textClipboard, IHostStorageProvider storageProvider, IStructureViewModelFactory structureViewModelFactory, bool canCloseTab = false, uint startAddress = 0, uint endAddress = A20Gate.EndOfHighMemoryArea) : base(textClipboard) {
        _pauseHandler = pauseHandler;
        _memory = memory;
        _pauseHandler.Pausing += OnPause;
        IsPaused = pauseHandler.IsPaused;
        pauseHandler.Resumed += () => IsPaused = false;
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

    /// <summary>
    /// Handles the event when the selection range within the HexEditor changes.
    /// </summary>
    /// <param name="sender">The source of the event, expected to be of type <see cref="Selection"/>.</param>
    /// <param name="e">The event arguments, not used in this method.</param>
    public void OnSelectionRangeChanged(object? sender, EventArgs e) {
        SelectionRange = (sender as Selection)?.Range;
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
            data = new ByteArrayBinaryDocument(bytes);
        } else {
            data = DataMemoryDocument;
        }
        StructureViewModel structureViewModel = _structureViewModelFactory.CreateNew(data);
        var structureWindow = new StructureView {DataContext = structureViewModel};
        structureWindow.Show();
    }
    
    private void OnPause() {
        IsPaused = true;
        UpdateBinaryDocument();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void NewMemoryView() {
        MemoryViewModel memoryViewModel = new(_memory, _pauseHandler, _messenger, _textClipboard,
            _storageProvider,
            _structureViewModelFactory, canCloseTab: true);
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
            await _storageProvider.SaveBinaryFile(_memory.GetData(StartAddress.Value, EndAddress.Value - StartAddress.Value));
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
        if (MemoryEditAddress is not null && TryParseMemoryAddress(MemoryEditAddress, out uint? memoryEditAddressValue)) {
            MemoryEditValue = Convert.ToHexString(_memory.GetData(memoryEditAddressValue.Value, (uint)(MemoryEditValue?.Length ?? sizeof(ushort))));
        }
    }

    private bool TryParseMemoryAddress(string? memoryAddress, [NotNullWhen(true)] out uint? address) {
        if (string.IsNullOrWhiteSpace(memoryAddress)) {
            address = null;
            return false;
        }

        try {
            if (memoryAddress.Contains(':')) {
                string[] split = memoryAddress.Split(":");
                if (split.Length > 1 &&
                    ushort.TryParse(split[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort segment) &&
                    ushort.TryParse(split[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort offset)) {
                    address = MemoryUtils.ToPhysicalAddress(segment, offset);

                    return true;
                }
            } else if (uint.TryParse(memoryAddress, CultureInfo.InvariantCulture, out uint value)) {
                address = value;

                return true;
            }
        } catch (Exception e) {
            Dispatcher.UIThread.Post(() => ShowError(e));
        }
        address = null;

        return false;
    }

    [RelayCommand]
    private void CancelMemoryEdit() => IsEditingMemory = false;

    [RelayCommand]
    private void ApplyMemoryEdit() {
        if (!TryParseMemoryAddress(MemoryEditAddress, out uint? address) ||
            MemoryEditValue is null ||
            !long.TryParse(MemoryEditValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value)) {
            return;
        }
        DataMemoryDocument?.WriteBytes(address.Value, BitConverter.GetBytes(value));
        IsEditingMemory = false;
    }
}