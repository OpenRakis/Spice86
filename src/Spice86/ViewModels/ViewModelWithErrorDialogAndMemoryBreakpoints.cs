namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.ViewModels.Services;

/// <summary>
/// Base class for ViewModels that need both error dialog and memory breakpoint functionality.
/// Combines the functionality of ViewModelWithErrorDialog with memory breakpoint support.
/// </summary>
public abstract partial class ViewModelWithErrorDialogAndMemoryBreakpoints : ViewModelWithErrorDialog {
    protected readonly State _state;
    protected readonly IMemory _memory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewModelWithErrorDialogAndMemoryBreakpoints"/> class.
    /// </summary>
    /// <param name="uiDispatcher">UI dispatcher for thread-safe UI operations.</param>
    /// <param name="textClipboard">Text clipboard for copy operations.</param>
    /// <param name="state">CPU state for address parsing.</param>
    /// <param name="memory">Memory interface for value condition checking.</param>
    protected ViewModelWithErrorDialogAndMemoryBreakpoints(
        IUIDispatcher uiDispatcher,
        ITextClipboard textClipboard,
        State state,
        IMemory memory) : base(uiDispatcher, textClipboard) {
        _state = state;
        _memory = memory;
    }

    private string? _memoryBreakpointStartAddress;

    /// <summary>
    /// Gets or sets the memory breakpoint start address.
    /// </summary>
    public string? MemoryBreakpointStartAddress {
        get => _memoryBreakpointStartAddress;
        set {
            SetProperty(ref _memoryBreakpointStartAddress, value);
            ValidateMemoryBreakpointForm();
            NotifyMemoryBreakpointCanExecuteChanged();
        }
    }

    private string? _memoryBreakpointEndAddress;

    /// <summary>
    /// Gets or sets the memory breakpoint end address.
    /// </summary>
    public string? MemoryBreakpointEndAddress {
        get => _memoryBreakpointEndAddress;
        set {
            SetProperty(ref _memoryBreakpointEndAddress, value);
            ValidateMemoryBreakpointForm();
            NotifyMemoryBreakpointCanExecuteChanged();
        }
    }

    private string? _memoryBreakpointValueCondition;

    /// <summary>
    /// Gets or sets the memory breakpoint value condition (hex bytes).
    /// </summary>
    public string? MemoryBreakpointValueCondition {
        get => _memoryBreakpointValueCondition;
        set {
            SetProperty(ref _memoryBreakpointValueCondition, value);
            ValidateMemoryBreakpointForm();
            NotifyMemoryBreakpointCanExecuteChanged();
        }
    }

    [ObservableProperty]
    private BreakPointType _selectedMemoryBreakpointType = BreakPointType.MEMORY_WRITE;

    /// <summary>
    /// Gets the available memory breakpoint types.
    /// </summary>
    public BreakPointType[] MemoryBreakpointTypes => [
        BreakPointType.MEMORY_WRITE, BreakPointType.MEMORY_READ, BreakPointType.MEMORY_ACCESS
    ];

    /// <summary>
    /// Validates the memory breakpoint form fields.
    /// </summary>
    private void ValidateMemoryBreakpointForm() {
        // Validate start address
        ValidateAddressProperty(_memoryBreakpointStartAddress, _state, nameof(MemoryBreakpointStartAddress));
        ValidateAddressRange(_state, _memoryBreakpointStartAddress, _memoryBreakpointEndAddress, 0,
            nameof(MemoryBreakpointStartAddress));
        ValidateMemoryAddressIsWithinLimit(_state, _memoryBreakpointStartAddress);
        AddressAndValueParser.TryParseAddressString(_memoryBreakpointStartAddress, _state, out uint? breakpointRangeStartAddress);

        // Validate end address
        ValidateAddressProperty(_memoryBreakpointEndAddress, _state, nameof(MemoryBreakpointEndAddress));
        ValidateAddressRange(_state, _memoryBreakpointStartAddress, _memoryBreakpointEndAddress, 0,
            nameof(MemoryBreakpointEndAddress));
        ValidateMemoryAddressIsWithinLimit(_state, _memoryBreakpointEndAddress);
        AddressAndValueParser.TryParseAddressString(_memoryBreakpointEndAddress, _state, out uint? breakpointRangeEndAddress);

        // Validate value condition
        int length = 1;
        if (breakpointRangeStartAddress != null && breakpointRangeEndAddress != null) {
            length = (int)(breakpointRangeEndAddress.Value - breakpointRangeStartAddress.Value) + 1;
        }

        ValidateHexProperty(_memoryBreakpointValueCondition, length, nameof(MemoryBreakpointValueCondition));
    }

    /// <summary>
    /// Checks if the memory breakpoint form has validation errors.
    /// </summary>
    /// <returns>True if there are no validation errors, false otherwise.</returns>
    protected bool HasNoMemoryBreakpointValidationErrors() {
        return !ScanForValidationErrors(
            nameof(MemoryBreakpointStartAddress),
            nameof(MemoryBreakpointEndAddress),
            nameof(MemoryBreakpointValueCondition));
    }

    /// <summary>
    /// Creates a memory breakpoint with the current form values.
    /// </summary>
    /// <param name="createBreakpointAction">Action to create the actual breakpoint with start, end addresses, type, and condition.</param>
    /// <returns>True if the breakpoint was created successfully, false otherwise.</returns>
    protected bool TryCreateMemoryBreakpointFromForm(Action<uint, uint, BreakPointType, Func<long, bool>?> createBreakpointAction) {
        if (!AddressAndValueParser.TryParseAddressString(_memoryBreakpointStartAddress, _state, out uint? startAddress) ||
            !startAddress.HasValue) {
            return false;
        }

        byte[]? triggerValueCondition = AddressAndValueParser.ParseHexAsArray(_memoryBreakpointValueCondition);
        Func<long, bool>? condition = CreateCheckForBreakpointMemoryValue(
            triggerValueCondition,
            startAddress.Value);

        if (AddressAndValueParser.TryParseAddressString(_memoryBreakpointEndAddress, _state, out uint? endAddress) &&
            endAddress.HasValue) {
            createBreakpointAction(startAddress.Value, endAddress.Value, SelectedMemoryBreakpointType, condition);
        } else {
            createBreakpointAction(startAddress.Value, startAddress.Value, SelectedMemoryBreakpointType, condition);
        }

        return true;
    }

    /// <summary>
    /// Creates a condition function that checks if memory values match the specified trigger condition.
    /// </summary>
    /// <param name="triggerValueCondition">The hex bytes to match against.</param>
    /// <param name="startAddress">The starting address of the breakpoint range.</param>
    /// <returns>A function that returns true if the memory value matches the condition, or null if no condition specified.</returns>
    private Func<long, bool>? CreateCheckForBreakpointMemoryValue(byte[]? triggerValueCondition, long startAddress) {
        if (triggerValueCondition is null || triggerValueCondition.Length == 0) {
            return null;
        }

        BreakPointType type = SelectedMemoryBreakpointType;

        return (long address) => {
            long index = address - startAddress;
            
            // Bounds checking to prevent IndexOutOfRangeException
            if (index < 0 || index >= triggerValueCondition.Length) {
                return false;
            }
            
            byte expectedValue = triggerValueCondition[index];

            // Add explicit parentheses to clarify operator precedence
            if (((type is BreakPointType.MEMORY_READ or BreakPointType.MEMORY_ACCESS) && _memory.SneakilyRead((uint)address) == expectedValue) ||
                ((type is BreakPointType.MEMORY_WRITE or BreakPointType.MEMORY_ACCESS) && _memory.CurrentlyWritingByte == expectedValue)) {
                return true;
            }

            return false;
        };
    }

    /// <summary>
    /// Notifies that the memory breakpoint command execution status should be re-evaluated.
    /// Derived classes should override this to notify their specific commands.
    /// </summary>
    protected abstract void NotifyMemoryBreakpointCanExecuteChanged();
}
