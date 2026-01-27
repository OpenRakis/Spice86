namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;

using System;

/// <summary>
/// View model for a CPU register value.
/// </summary>
public partial class RegisterViewModel : ObservableObject {
    private readonly State _state;
    private readonly Func<State, uint> _valueGetter;
    private uint _previousValue;

    /// <summary>
    /// Gets the name of the register.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the current value of the register.
    /// </summary>
    [ObservableProperty]
    private uint _value;

    /// <summary>
    /// Gets the hexadecimal representation of the register value.
    /// </summary>
    [ObservableProperty]
    private string _hexValue;

    /// <summary>
    /// Gets a value indicating whether the register value has changed since the last update.
    /// </summary>
    [ObservableProperty]
    private bool _hasChanged;

    /// <summary>
    /// Gets a value indicating whether the low byte (bits 0-7) has changed.
    /// </summary>
    [ObservableProperty]
    private bool _lowByteChanged;

    /// <summary>
    /// Gets a value indicating whether the high byte (bits 8-15) has changed.
    /// </summary>
    [ObservableProperty]
    private bool _highByteChanged;

    /// <summary>
    /// Gets a value indicating whether the upper word (bits 16-31) has changed.
    /// </summary>
    [ObservableProperty]
    private bool _upperWordChanged;

    /// <summary>
    /// Gets a value indicating whether the lower word (bits 0-15) has changed.
    /// </summary>
    [ObservableProperty]
    private bool _lowerWordChanged;

    /// <summary>
    /// Gets the low byte (bits 0-7) in hexadecimal.
    /// </summary>
    [ObservableProperty]
    private string _lowByteHex;

    /// <summary>
    /// Gets the high byte (bits 8-15) in hexadecimal.
    /// </summary>
    [ObservableProperty]
    private string _highByteHex;

    /// <summary>
    /// Gets the upper word (bits 16-31) in hexadecimal.
    /// </summary>
    [ObservableProperty]
    private string _upperWordHex;

    /// <summary>
    /// Gets the lower word (bits 0-15) in hexadecimal.
    /// </summary>
    [ObservableProperty]
    private string _lowerWordHex;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegisterViewModel"/> class.
    /// </summary>
    /// <param name="name">The name of the register.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="valueGetter">Function to get the register value from the CPU state.</param>
    /// <param name="bitSize">The size of the register in bits (32, 16, or 8). Defaults to 32.</param>
    public RegisterViewModel(string name, State state, Func<State, uint> valueGetter, int bitSize = 32) {
        Name = name;
        _state = state;
        _valueGetter = valueGetter;
        BitSize = bitSize;
        _value = _valueGetter(state);
        _previousValue = _value;
        _hexValue = FormatValue(_value, BitSize);
        _upperWordHex = $"{_value >> 16 & 0xFFFF:X4}";
        _lowerWordHex = $"{_value & 0xFFFF:X4}";
        _highByteHex = $"{_value >> 8 & 0xFF:X2}";
        _lowByteHex = $"{_value & 0xFF:X2}";
    }

    /// <summary>
    /// Gets the size of the register in bits.
    /// </summary>
    public int BitSize { get; }

    private static string FormatValue(uint value, int bitSize) {
        return bitSize switch {
            32 => $"{value:X8}",
            16 => $"{value:X4}",
            8 => $"{value:X2}",
            _ => $"{value:X4}"
        };
    }

    /// <summary>
    /// Updates the register value from the CPU state and checks if it has changed.
    /// </summary>
    public void Update() {
        _previousValue = Value;
        Value = _valueGetter(_state);

        uint changedBits = _previousValue ^ Value;

        // Check overall change
        HasChanged = changedBits != 0;
        if (!HasChanged) {
            UpperWordChanged = false;
            LowerWordChanged = false;
            HighByteChanged = false;
            LowByteChanged = false;

            return;
        }
        HexValue = FormatValue(Value, BitSize);

        // Check upper word
        UpperWordChanged = (changedBits & 0xFFFF0000) != 0;
        if (UpperWordChanged) {
            UpperWordHex = $"{Value >> 16 & 0xFFFF:X4}";
        }

        // Check lower word
        LowerWordChanged = (changedBits & 0xFFFF) != 0;
        if (!LowerWordChanged) {
            HighByteChanged = false;
            LowByteChanged = false;

            return;
        }
        LowerWordHex = $"{Value & 0xFFFF:X4}";

        // Check high byte
        HighByteChanged = (changedBits & 0xFF00) != 0;
        if (HighByteChanged) {
            HighByteHex = $"{Value >> 8 & 0xFF:X2}";
        }

        // Check low byte
        LowByteChanged = (changedBits & 0xFF) != 0;
        if (LowByteChanged) {
            LowByteHex = $"{Value & 0xFF:X2}";
        }
    }
}