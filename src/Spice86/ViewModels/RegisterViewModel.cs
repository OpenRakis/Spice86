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
    public uint Value { get; private set; }

    /// <summary>
    /// Gets the hexadecimal representation of the register value.
    /// </summary>
    public string HexValue => $"{Value:X4}";

    /// <summary>
    /// Gets a value indicating whether the register value has changed since the last update.
    /// </summary>
    public bool HasChanged { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the low byte (bits 0-7) has changed.
    /// </summary>
    public bool LowByteChanged { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the high byte (bits 8-15) has changed.
    /// </summary>
    public bool HighByteChanged { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the upper word (bits 16-31) has changed.
    /// </summary>
    public bool UpperWordChanged { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the lower word (bits 0-15) has changed.
    /// </summary>
    public bool LowerWordChanged { get; private set; }

    /// <summary>
    /// Gets the low byte (bits 0-7) in hexadecimal.
    /// </summary>
    public string LowByteHex => $"{Value & 0xFF:X2}";

    /// <summary>
    /// Gets the high byte (bits 8-15) in hexadecimal.
    /// </summary>
    public string HighByteHex => $"{Value >> 8 & 0xFF:X2}";

    /// <summary>
    /// Gets the upper word (bits 16-31) in hexadecimal.
    /// </summary>
    public string UpperWordHex => $"{Value >> 16 & 0xFFFF:X4}";

    /// <summary>
    /// Gets the lower word (bits 0-15) in hexadecimal.
    /// </summary>
    public string LowerWordHex => $"{Value & 0x0000FFFF:X4}";

    /// <summary>
    /// Initializes a new instance of the <see cref="RegisterViewModel"/> class.
    /// </summary>
    /// <param name="name">The name of the register.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="valueGetter">Function to get the register value from the CPU state.</param>
    public RegisterViewModel(string name, State state, Func<State, uint> valueGetter) {
        Name = name;
        _state = state;
        _valueGetter = valueGetter;
        Value = _valueGetter(state);
        _previousValue = Value;
        HasChanged = false;
        LowByteChanged = false;
        HighByteChanged = false;
        UpperWordChanged = false;
        LowerWordChanged = false;
    }

    /// <summary>
    /// Updates the register value from the CPU state and checks if it has changed.
    /// </summary>
    public void Update() {
        _previousValue = Value;
        Value = _valueGetter(_state);

        // Check overall change
        HasChanged = _previousValue != Value;

        // Check byte-level changes
        LowByteChanged = (Value & 0xFF) != (_previousValue & 0xFF);
        HighByteChanged = (Value >> 8 & 0xFF) != (_previousValue >> 8 & 0xFF);
        UpperWordChanged = (Value >> 16 & 0xFFFF) != (_previousValue >> 16 & 0xFFFF);
        LowerWordChanged = (Value & 0x0000FFFF) != (_previousValue & 0x0000FFFF);

        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(HexValue));
        OnPropertyChanged(nameof(HasChanged));
        OnPropertyChanged(nameof(LowByteHex));
        OnPropertyChanged(nameof(HighByteHex));
        OnPropertyChanged(nameof(UpperWordHex));
        OnPropertyChanged(nameof(LowerWordHex));
        OnPropertyChanged(nameof(LowByteChanged));
        OnPropertyChanged(nameof(HighByteChanged));
        OnPropertyChanged(nameof(UpperWordChanged));
        OnPropertyChanged(nameof(LowerWordChanged));
    }

    /// <summary>
    /// Resets the change detection.
    /// </summary>
    public void ResetChangeDetection() {
        _previousValue = Value;
        HasChanged = false;
        LowByteChanged = false;
        HighByteChanged = false;
        UpperWordChanged = false;
        LowerWordChanged = false;

        OnPropertyChanged(nameof(HasChanged));
        OnPropertyChanged(nameof(LowByteChanged));
        OnPropertyChanged(nameof(HighByteChanged));
        OnPropertyChanged(nameof(UpperWordChanged));
        OnPropertyChanged(nameof(LowerWordChanged));
    }
}