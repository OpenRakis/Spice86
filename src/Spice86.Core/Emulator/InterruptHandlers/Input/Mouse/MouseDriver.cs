namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Interfaces;

/// <summary>
///     Driver for the mouse.
/// </summary>
public class MouseDriver : IMouseDriver {
    private class MouseButtonPressCount {
        public int PressCount { get; set; }
        public double LastPressedX { get; set; }
        public double LastPressedY { get; set; }
    }

    private readonly Dictionary<MouseButton, MouseButtonPressCount> _buttonsPressCounts = new() {
        { MouseButton.Left, new MouseButtonPressCount() },
        { MouseButton.Right, new MouseButtonPressCount() },
        { MouseButton.Middle, new MouseButtonPressCount() }
    };

    private readonly Dictionary<MouseButton, MouseButtonPressCount> _buttonsReleaseCounts = new() {
        { MouseButton.Left, new MouseButtonPressCount() },
        { MouseButton.Right, new MouseButtonPressCount() },
        { MouseButton.Middle, new MouseButtonPressCount() }
    };

    private const int VirtualScreenWidth = 640;
    private readonly IGuiMouseEvents? _gui;
    private readonly ILoggerService _logger;
    private readonly IMouseDevice _mouseDevice;
    private readonly State _state;
    private readonly SharedMouseData _sharedMouseData;

    private readonly IVgaFunctionality _vgaFunctions;
    private readonly InMemoryAddressSwitcher _userHandlerAddressSwitcher;

    private int _mouseCursorHidden;
    private MouseRegisters? _savedRegisters;
    private MouseUserCallback _userCallback;
    private VgaMode _vgaMode;
    private bool _userHandlerIsBeingCalled;

    /// <summary>
    ///     Create a new instance of the mouse driver.
    /// </summary>
    /// <param name="state">CPU registers. Used to save/restore registers</param>
    /// <param name="sharedMouseData">Shared properties with the UI for display.</param>
    /// <param name="memory">Memory instance to look into the interrupt vector table</param>
    /// <param name="mouseDevice">The mouse device / hardware</param>
    /// <param name="vgaFunctions">Access to the current resolution</param>
    /// <param name="loggerService">The service used to log messages, such as runtime warnings.</param>
    /// <param name="gui">Optional GUI mouse events interface for UI integration.</param>
    public MouseDriver(State state, SharedMouseData sharedMouseData,
        IIndexable memory, IMouseDevice mouseDevice,
        IVgaFunctionality vgaFunctions, ILoggerService loggerService,
        IGuiMouseEvents? gui = null) {
        _state = state;
        _sharedMouseData = sharedMouseData;
        _logger = loggerService;
        _mouseDevice = mouseDevice;
        _gui = gui;
        if (_gui is not null) {
            _gui.MouseButtonUp += OnMouseButtonReleased;
            _gui.MouseButtonDown += OnMouseButtonPressed;
        }
        _vgaFunctions = vgaFunctions;

        _vgaFunctions.VideoModeChanged += OnVideoModeChanged;
        _userHandlerAddressSwitcher = new InMemoryAddressSwitcher(memory);
        Reset();
    }

    private void OnMouseButtonReleased(object? sender, MouseButtonEventArgs e) {
        MouseButton button = e.Button;
        if (!_buttonsReleaseCounts.TryGetValue(button, out MouseButtonPressCount? entry)) {
            return;
        }

        entry.PressCount++;
        entry.LastPressedX = _mouseDevice.MouseXRelative;
        entry.LastPressedY = _mouseDevice.MouseYRelative;
    }

    private void OnMouseButtonPressed(object? sender, MouseButtonEventArgs e) {
        MouseButton button = e.Button;
        if (!_buttonsPressCounts.TryGetValue(button, out MouseButtonPressCount? entry)) {
            return;
        }

        entry.PressCount++;
        entry.LastPressedX = _mouseDevice.MouseXRelative;
        entry.LastPressedY = _mouseDevice.MouseYRelative;
    }

    /// <inheritdoc />
    public int CurrentMinX { get => _sharedMouseData.CurrentMinX; set => _sharedMouseData.CurrentMinX = value; }

    /// <inheritdoc />
    public int CurrentMaxX { get => _sharedMouseData.CurrentMaxX; set => _sharedMouseData.CurrentMaxX = value; }

    /// <inheritdoc />
    public int CurrentMaxY { get => _sharedMouseData.CurrentMaxY; set => _sharedMouseData.CurrentMaxY = value; }

    /// <inheritdoc />
    public int CurrentMinY { get => _sharedMouseData.CurrentMinY; set => _sharedMouseData.CurrentMinY = value; }

    /// <inheritdoc />
    public void BeforeUserHandlerExecution() {
        if (CanCallUserRoutine()) {
            PrepareUserRoutineCall();
            EnsureUserRoutineWillBeCalledNextInstruction();
        } else {
            EnsureUserRoutineWillBeNotCalledNextInstruction();
        }
    }

    private bool CanCallUserRoutine() {
        if ((_mouseDevice.LastTrigger & _userCallback.TriggerMask) == 0) {
            // No new matching events
            return false;
        }
        if (_userCallback.Segment == 0 || _userCallback.Offset == 0) {
            // User callback disabled
            return false;
        }

        return !_userHandlerIsBeingCalled;
        // Call already in progress
    }

    private void PrepareUserRoutineCall() {
        _userHandlerIsBeingCalled = true;
        // Re-enable interrupts to allow for higher prio ints to occur (sound)
        _state.InterruptFlag = true;
        // Save registers so that we can restore them later when user routine is done
        SaveRegisters();
        // User handler is going to be called. Save registers, set them to their new values
        SetRegistersToMouseState();
    }

    private void EnsureUserRoutineWillBeCalledNextInstruction() {
        // Address is written to the call instruction that is supposed to be just after this code runs.
        _userHandlerAddressSwitcher.SetAddress(_userCallback.Segment, _userCallback.Offset);
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} {MethodName}: calling {Segment:X4}:{Offset:X4} with AX={AX:X4}, BX={BX:X4}, CX={CX:X4}, DX={DX:X4}, SI={SI:X4}, DI={DI:X4}",
                nameof(MouseDriver), nameof(BeforeUserHandlerExecution), _userCallback.Segment, _userCallback.Offset, _state.AX, _state.BX, _state.CX, _state.DX, _state.SI, _state.DI);
        }
    }

    private void EnsureUserRoutineWillBeNotCalledNextInstruction() {
        // User routine needs to be disabled because it is called unconditionally in interrupt handler ASM. Disabling means calling an empty function.
        _userHandlerAddressSwitcher.SetAddressToDefault();
    }

    /// <inheritdoc />
    public MouseUserCallback GetRegisteredCallback() {
        return _userCallback;
    }

    /// <inheritdoc />
    public void RegisterCallback(MouseUserCallback callbackInfo) {
        _userCallback = callbackInfo;
    }

    /// <inheritdoc />
    public void ShowMouseCursor() {
        if (_mouseCursorHidden != 0) {
            _mouseCursorHidden++;
        }
        if (_mouseCursorHidden == 0) {
            _gui?.ShowMouseCursor();
        }
    }

    /// <inheritdoc />
    public void HideMouseCursor() {
        if (_mouseCursorHidden == 0) {
            _gui?.HideMouseCursor();
        }
        _mouseCursorHidden--;
    }

    /// <inheritdoc />
    public void SetCursorPosition(int x, int y) {
        int mouseAreaWidth = CurrentMaxX - CurrentMinX;
        int mouseAreaHeight = CurrentMaxY - CurrentMinY;
        
        int clampedX = Math.Clamp(x, CurrentMinX, CurrentMaxX);
        int clampedY = Math.Clamp(y, CurrentMinY, CurrentMaxY);
        
        if (mouseAreaWidth <= 0) {
            _mouseDevice.MouseXRelative = 0.0;
        } else {
            _mouseDevice.MouseXRelative = (double)(clampedX - CurrentMinX) / mouseAreaWidth;
        }

        if (mouseAreaHeight <= 0) {
            _mouseDevice.MouseYRelative = 0.0;
        } else {
            _mouseDevice.MouseYRelative = (double)(clampedY - CurrentMinY) / mouseAreaHeight;
        }

        // This does not do anything in Avalonia, but it could in a different UI.
        if (_gui != null) {
            _gui.MouseX = clampedX;
            _gui.MouseY = clampedY;
        }
    }

    /// <inheritdoc />
    public int ButtonCount => _mouseDevice.ButtonCount;

    /// <inheritdoc />
    public MouseType MouseType => _mouseDevice.MouseType;

    /// <inheritdoc />
    public MouseStatusRecord CurrentMouseStatus {
        get {
            return _sharedMouseData.CurrentMouseStatus;
        }
    }

    /// <inheritdoc />
    public double GetLastReleasedX(MouseButton button) {
        return _buttonsReleaseCounts.TryGetValue(button,
            out MouseButtonPressCount? position) ? position.LastPressedX : 0;
    }

    /// <inheritdoc />
    public double GetLastReleasedY(MouseButton button) {
        return _buttonsReleaseCounts.TryGetValue(button,
            out MouseButtonPressCount? position)
            ? position.LastPressedY
            : 0;
    }

    /// <inheritdoc />
    public int GetButtonsReleaseCount(MouseButton button) {
        if (!_buttonsReleaseCounts.TryGetValue(button, out MouseButtonPressCount? value)) {
            return 0;
        }

        int count = value.PressCount;
        // Reset the count after reading
        value.PressCount = 0;
        return count;
    }

    /// <inheritdoc />
    public int GetButtonPressCount(MouseButton button) {
        if (!_buttonsPressCounts.TryGetValue(button, out MouseButtonPressCount? value)) {
            return 0;
        }

        int count = value.PressCount;
        // Reset the count after reading
        value.PressCount = 0;
        return count;
    }

    /// <inheritdoc />
    public double GetLastPressedX(MouseButton button) {
        return _buttonsPressCounts.TryGetValue(button,
            out MouseButtonPressCount? position) ? position.LastPressedX : 0;
    }

    /// <inheritdoc />
    public double GetLastPressedY(MouseButton button) {
        return _buttonsPressCounts.TryGetValue(button,
            out MouseButtonPressCount? position)
            ? position.LastPressedY
            : 0;
    }

    /// <inheritdoc />
    public int HorizontalMickeysPerPixel {
        get => _mouseDevice.HorizontalMickeysPerPixel;
        set => _mouseDevice.HorizontalMickeysPerPixel = value;
    }

    /// <inheritdoc />
    public int VerticalMickeysPerPixel {
        get => _mouseDevice.VerticalMickeysPerPixel;
        set => _mouseDevice.VerticalMickeysPerPixel = value;
    }

    /// <inheritdoc />
    public int DoubleSpeedThreshold {
        get => _mouseDevice.DoubleSpeedThreshold;
        set => _mouseDevice.DoubleSpeedThreshold = value;
    }

    /// <inheritdoc />
    public void AfterUserHandlerExecution() {
        RestoreRegisters();
        _userHandlerIsBeingCalled = false;
    }

    /// <inheritdoc />
    public short GetDeltaXMickeys() {
        double deltaXPixels = _mouseDevice.DeltaX * VirtualScreenWidth;
        double deltaXMickeys = _mouseDevice.HorizontalMickeysPerPixel * deltaXPixels;
        return (short)deltaXMickeys;
    }

    /// <inheritdoc />
    public short GetDeltaYMickeys() {
        double deltaYPixels = _mouseDevice.DeltaY * _vgaMode.Height;
        double deltaYMickeys = _mouseDevice.VerticalMickeysPerPixel * deltaYPixels;
        return (short)deltaYMickeys;
    }

    /// <inheritdoc />
    public void ResetDeltaMickeys() {
        _mouseDevice.ResetDeltas();
    }

    /// <inheritdoc />
    public void Reset() {
        _vgaMode = _vgaFunctions.GetCurrentMode();
        SetCursorPosition(VirtualScreenWidth / 2, _vgaMode.Height / 2);
        _mouseCursorHidden = -1;
        _gui?.HideMouseCursor();
        CurrentMinX = 0;
        CurrentMinY = 0;
        CurrentMaxX = VirtualScreenWidth - 1;
        CurrentMaxY = _vgaMode.Height - 1;
        HorizontalMickeysPerPixel = 8;
        VerticalMickeysPerPixel = 16;
        DoubleSpeedThreshold = 64;

        ResetCounters(_buttonsPressCounts);
        ResetCounters(_buttonsReleaseCounts);
    }

    private static void ResetCounters(Dictionary<MouseButton, MouseButtonPressCount> dict) {
        foreach (MouseButtonPressCount entry in dict.Values) {
            entry.PressCount = 0;
            entry.LastPressedX = 0;
            entry.LastPressedY = 0;
        }
    }

    private void OnVideoModeChanged(object? sender, VideoModeChangedEventArgs e) {
        Reset();
    }

    private void SetRegistersToMouseState() {
        // Set mouse info
        MouseStatusRecord status = CurrentMouseStatus;
        _state.AX = (ushort)_mouseDevice.LastTrigger;
        _state.BX = (ushort)status.ButtonFlags;
        _state.CX = (ushort)status.X;
        _state.DX = (ushort)status.Y;
        _state.SI = (ushort)GetDeltaXMickeys();
        _state.DI = (ushort)GetDeltaYMickeys();
    }

    private void SaveRegisters() {
        _savedRegisters = new MouseRegisters(_state.ES, _state.DS, _state.DI,
        _state.SI, _state.BP, _state.SP, _state.BX, _state.DX,
        _state.CX, _state.AX);
    }

    private void RestoreRegisters() {
        if (_savedRegisters == null) {
            return;
        }
        _state.ES = _savedRegisters.Es;
        _state.DS = _savedRegisters.Ds;
        _state.DI = _savedRegisters.Di;
        _state.SI = _savedRegisters.Si;
        _state.BP = _savedRegisters.Bp;
        _state.SP = _savedRegisters.Sp;
        _state.BX = _savedRegisters.Bx;
        _state.DX = _savedRegisters.Dx;
        _state.CX = _savedRegisters.Cx;
        _state.AX = _savedRegisters.Ax;
        // Make sure we cant restore them twice.
        // This could happen when a program disables the mouse via event mask. In this case, save would not be called and we would create a mess by restoring old values unrelated to the call.
        _savedRegisters = null;
    }

    private record MouseRegisters(ushort Es, ushort Ds, ushort Di, ushort Si, ushort Bp, ushort Sp, ushort Bx, ushort Dx, ushort Cx, ushort Ax);

    /// <inheritdoc />
    public SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        // Mouse driver implementation:
        //  - Create a FAR ret which is the default user handler (called when program does not provide anything else)
        //  - Create a callback (0xFE) that will call the BeforeUserHandlerExecution method
        //  - Create a modifiable Far call instruction that is calling the default handler
        //  - Create a callback (0xFF) that does the cleanup with AfterUserHandlerExecution
        //  - Create a FAR ret

        // Write ASM
        // Default user handler: nothing, just a far ret. 
        _userHandlerAddressSwitcher.DefaultAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.WriteFarRet();

        SegmentedAddress driverAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.RegisterAndWriteCallback(BeforeUserHandlerExecution);
        // Far call to default handler, can be changed via _inMemoryAddressSwitcher
        memoryAsmWriter.WriteFarCallToSwitcherDefaultAddress(_userHandlerAddressSwitcher);
        memoryAsmWriter.RegisterAndWriteCallback(AfterUserHandlerExecution);
        // Far ret to return to caller
        memoryAsmWriter.WriteFarRet();
        return driverAddress;
    }
}