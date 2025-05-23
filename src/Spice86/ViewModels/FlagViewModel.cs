namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// View model for a CPU flag value.
/// </summary>
public partial class FlagViewModel : ObservableObject {
    private readonly State _state;
    private readonly Func<State, bool> _valueGetter;
    private bool _previousValue;

    /// <summary>
    /// Gets the name of the flag.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the current value of the flag.
    /// </summary>
    [ObservableProperty]
    private bool _value;

    /// <summary>
    /// Gets a value indicating whether the flag value has changed since the last update.
    /// </summary>
    [ObservableProperty]
    private bool _hasChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlagViewModel"/> class.
    /// </summary>
    /// <param name="name">The name of the flag.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="valueGetter">Function to get the flag value from the CPU state.</param>
    public FlagViewModel(string name, State state, Func<State, bool> valueGetter) {
        Name = name;
        _state = state;
        _valueGetter = valueGetter;
        _value = _valueGetter(state);
        _previousValue = _value;
        _hasChanged = false;
    }

    /// <summary>
    /// Updates the flag value from the CPU state and checks if it has changed.
    /// </summary>
    public void Update() {
        _previousValue = Value;
        Value = _valueGetter(_state);
        HasChanged = _previousValue != Value;
    }
}