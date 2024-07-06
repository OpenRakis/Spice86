namespace Spice86.ViewModels;

using Avalonia.Threading;

using AvaloniaHex.Document;
using AvaloniaHex.Editing;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Core.Emulator.Memory;
using Spice86.Infrastructure;
using Spice86.Interfaces;
using Spice86.MemoryWrappers;
using Spice86.Shared.Utils;
using Spice86.Views;

using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

public partial class MemoryViewModel : ViewModelBaseWithErrorDialog, IInternalDebugger {
    private IMemory? _memory;
    private readonly DebugWindowViewModel _debugWindowViewModel;
    private bool _needToUpdateBinaryDocument;
    private readonly IStructureViewModelFactory _structureViewModelFactory;

    [ObservableProperty]
    private DataMemoryDocument? _memoryBinaryDocument;

    public bool NeedsToVisitEmulator => _memory is null;

    private uint? _startAddress = 0;

    [NotifyCanExecuteChangedFor(nameof(UpdateBinaryDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DumpMemoryCommand))]
    [ObservableProperty]
    private bool _isMemoryRangeValid;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    private bool _canCloseTab;

    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab() {
        _debugWindowViewModel.CloseTab(this);
        UpdateCanCloseTabProperty();
    }

    private bool GetIsMemoryRangeValid() {
        if (_memory is null) {
            return false;
        }

        return StartAddress <= (EndAddress ?? _memory.Length)
            && EndAddress >= (StartAddress ?? 0);
    }

    public uint? StartAddress {
        get => _startAddress;
        set {
            SetProperty(ref _startAddress, value);
            IsMemoryRangeValid = GetIsMemoryRangeValid();
            TryUpdateHeaderAndMemoryDocument();
        }
    }

    private void TryUpdateHeaderAndMemoryDocument() {
        if (!UpdateBinaryDocumentCommand.CanExecute(null)) {
            return;
        }
        Header = $"{StartAddress:X} - {EndAddress:X}";
        UpdateBinaryDocumentCommand.Execute(null);
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

    private readonly IPauseStatus _pauseStatus;
    private readonly IHostStorageProvider _storageProvider;

    public MemoryViewModel(DebugWindowViewModel debugWindowViewModel, ITextClipboard textClipboard, IUIDispatcherTimerFactory dispatcherTimerFactory, IHostStorageProvider storageProvider, IPauseStatus pauseStatus, uint startAddress, IStructureViewModelFactory structureViewModelFactory, uint endAddress = 0) : base(textClipboard) {
        _debugWindowViewModel = debugWindowViewModel;
        pauseStatus.PropertyChanged += PauseStatus_PropertyChanged;
        _pauseStatus = pauseStatus;
        _storageProvider = storageProvider;
        IsPaused = _pauseStatus.IsPaused;
        StartAddress = startAddress;
        _structureViewModelFactory = structureViewModelFactory;
        EndAddress = endAddress;
        dispatcherTimerFactory.StartNew(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, UpdateValues);
        UpdateCanCloseTabProperty();
        debugWindowViewModel.MemoryViewModels.CollectionChanged += OnDebugViewModelCollectionChanged;
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
        if (MemoryBinaryDocument == null) {
            return;
        }

        // Use either the selected range or the entire document if no range is selected.
        IBinaryDocument data;
        if (SelectionRange is {ByteLength: > 1} bitRange) {
            byte[] bytes = new byte[bitRange.ByteLength];
            MemoryBinaryDocument.ReadBytes(bitRange.Start.ByteIndex, bytes);
            data = new ByteArrayBinaryDocument(bytes);
        } else {
            data = MemoryBinaryDocument;
        }
        StructureViewModel structureViewModel = _structureViewModelFactory.CreateNew(data);
        var structureWindow = new StructureView {DataContext = structureViewModel};
        structureWindow.Show();
    }

    private void UpdateCanCloseTabProperty() {
        CanCloseTab = _debugWindowViewModel.MemoryViewModels.Count > 1;
    }

    private void OnDebugViewModelCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        UpdateCanCloseTabProperty();
    }

    private void UpdateValues(object? sender, EventArgs e) {
        if (!_needToUpdateBinaryDocument) {
            return;
        }
        UpdateBinaryDocument();
        _needToUpdateBinaryDocument = false;
    }

    private void PauseStatus_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName != nameof(IPauseStatus.IsPaused)) {
            return;
        }
        UpdateCanCloseTabProperty();
        IsPaused = _pauseStatus.IsPaused;
        if (IsPaused) {
            _needToUpdateBinaryDocument = true;
        }
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    public void NewMemoryView() {
        _debugWindowViewModel.NewMemoryViewCommand.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(IsMemoryRangeValid))]
    private void UpdateBinaryDocument() {
        if (_memory is null || StartAddress is null || EndAddress is null) {
            return;
        }
        MemoryBinaryDocument = new DataMemoryDocument(_memory, StartAddress.Value, EndAddress.Value);
        MemoryBinaryDocument.MemoryReadInvalidOperation -= OnMemoryReadInvalidOperation;
        MemoryBinaryDocument.MemoryReadInvalidOperation += OnMemoryReadInvalidOperation;
    }

    private void OnMemoryReadInvalidOperation(Exception exception) {
        Dispatcher.UIThread.Post(() => ShowError(exception));
    }

    [RelayCommand(CanExecute = nameof(IsMemoryRangeValid))]
    private async Task DumpMemory() {
        if (_memory is not null && StartAddress is not null && EndAddress is not null) {
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
    public void EditMemory() {
        IsEditingMemory = true;
        if (_memory is not null && MemoryEditAddress is not null && TryParseMemoryAddress(MemoryEditAddress, out uint? memoryEditAddressValue)) {
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
    public void CancelMemoryEdit() => IsEditingMemory = false;

    [RelayCommand]
    public void ApplyMemoryEdit() {
        if (!TryParseMemoryAddress(MemoryEditAddress, out uint? address) ||
            MemoryEditValue is null ||
            _memory is null ||
            !long.TryParse(MemoryEditValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value)) {
            return;
        }
        MemoryBinaryDocument?.WriteBytes(address.Value, BitConverter.GetBytes(value));
        IsEditingMemory = false;
    }

    public void Visit<T>(T component) where T : IDebuggableComponent {
        if (component is not IMemory memory) {
            return;
        }

        if (_memory is null) {
            _memory = memory;
            if (EndAddress is 0) {
                EndAddress = _memory.Length;
            }
        }
        TryUpdateHeaderAndMemoryDocument();
    }
}