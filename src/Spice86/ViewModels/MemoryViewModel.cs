﻿namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Core.Emulator.Memory;
using Spice86.Infrastructure;
using Spice86.Interfaces;
using Spice86.MemoryWrappers;
using Spice86.Shared.Utils;

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

public partial class MemoryViewModel : ViewModelBase, IInternalDebugger {
    private IMemory? _memory;
    [ObservableProperty]
    private MemoryBinaryDocument? _memoryBinaryDocument;

    public uint StartAddress { get; }

    [ObservableProperty]
    private uint _endAddress;

    [ObservableProperty]
    private string _header = "Memory Range";

    [ObservableProperty]
    private bool _isPaused;

    private readonly IPauseStatus _pauseStatus;

    public MemoryViewModel(IPauseStatus pauseStatus, ITextClipboard? textClipboard, uint startAddress, uint endAddress = 0) : base(textClipboard) {
        pauseStatus.PropertyChanged += PauseStatus_PropertyChanged;
        _pauseStatus = pauseStatus;
        IsPaused = _pauseStatus.IsPaused;
        StartAddress = startAddress;
        EndAddress = endAddress;
    }

    private void PauseStatus_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName != nameof(IPauseStatus.IsPaused)) {
            return;
        }
        IsPaused = _pauseStatus.IsPaused;
        if(IsPaused) {
            UpdateBinaryDocument();
        }
    }

    [RelayCommand]
    private void UpdateBinaryDocument() {
        if (_memory is not null) {
            MemoryBinaryDocument = new MemoryBinaryDocument(_memory, StartAddress, EndAddress);
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
            ShowError(e);
        }
        address = null;
        return false;
    }

    [RelayCommand]
    public void CancelMemoryEdit() {
        IsEditingMemory = false;
    }

    [RelayCommand]
    public void ApplyMemoryEdit() {
        if (!TryParseMemoryAddress(MemoryEditAddress, out uint? address) ||
            MemoryEditValue is null ||
            !long.TryParse(MemoryEditValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value)) {
            return;
        }
        MemoryBinaryDocument?.WriteBytes(address.Value, BitConverter.GetBytes(value));
        UpdateBinaryDocument();
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
                Header = $"{StartAddress:X} - {EndAddress:X}";
            }
        }
        MemoryBinaryDocument ??= new MemoryBinaryDocument(memory, StartAddress, EndAddress == 0 ? memory.Length : EndAddress);
    }
}
