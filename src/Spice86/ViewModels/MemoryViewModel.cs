namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Memory;
using Spice86.Interfaces;
using Spice86.MemoryWrappers;
using Spice86.Shared.Utils;

using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

public partial class MemoryViewModel : ViewModelBase {
    private readonly IMemory _memory;
    [ObservableProperty]
    private MemoryBinaryDocument _memoryBinaryDocument;

    [ObservableProperty]
    private bool _isPaused;

    private readonly IPauseStatus _pauseStatus;

    public MemoryViewModel(IMemory memory, IPauseStatus pauseStatus) {
        _memory = memory;
        _memoryBinaryDocument = new MemoryBinaryDocument(memory);
        pauseStatus.PropertyChanged += PauseStatus_PropertyChanged;
        _pauseStatus = pauseStatus;
        IsPaused = _pauseStatus.IsPaused;
    }

    private void PauseStatus_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if(e.PropertyName == nameof(IPauseStatus.IsPaused)) {
            IsPaused = _pauseStatus.IsPaused;
        }
    }

    public void UpdateBinaryDocument() {
        MemoryBinaryDocument = new MemoryBinaryDocument(_memory);
    }

    public void WriteBytes(ulong offset, ReadOnlySpan<byte> buffer) {
        MemoryBinaryDocument.WriteBytes(offset, buffer);
    }


    [ObservableProperty] private bool _isEditingMemory;

    [ObservableProperty]
    private string? _memoryEditAddress;

    [ObservableProperty]
    private string? _memoryEditValue = "";

    [RelayCommand(CanExecute = nameof(IsPaused))]
    public void EditMemory() {
        IsEditingMemory = true;
        if (MemoryEditAddress is not null && TryParseMemoryAddress(MemoryEditAddress, out uint? memoryEditAddressValue)) {
            MemoryEditValue = Convert.ToHexString(_memory.GetData(memoryEditAddressValue.Value, (uint)(MemoryEditValue is null ? sizeof(ushort) : MemoryEditValue.Length)));
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
        MemoryBinaryDocument.WriteBytes(address.Value, BitConverter.GetBytes(value));
        UpdateBinaryDocument();
        IsEditingMemory = false;
    }
}
