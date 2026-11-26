using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;


using System.Collections.Immutable;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
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
    private readonly Dictionary<MouseButton, MouseButtonCounterState> _buttonsPressCounts = new() {
        { MouseButton.Left, default },
        { MouseButton.Right, default },
        { MouseButton.Middle, default }
    };

    private readonly Dictionary<MouseButton, MouseButtonCounterState> _buttonsReleaseCounts = new() {
        { MouseButton.Left, default },
        { MouseButton.Right, default },
        { MouseButton.Middle, default }
    };

    private const int VirtualScreenWidth = 640;
    private readonly IGuiMouseEvents? _gui;
    private readonly ILogger _logger;
    private readonly IMouseDevice _mouseDevice;
    private readonly State _state;
    private readonly SharedMouseData _sharedMouseData;

    private readonly IVgaFunctionality _vgaFunctions;
    private readonly InMemoryAddressSwitcher _userHandlerAddressSwitcher;

    // One entry per driver pass in progress, innermost last: whether that pass called the user routine.
    // The user routine runs with interrupts enabled, so IRQ 12 can re-enter the driver stub while it runs.
    private readonly Stack<bool> _passCalledUserRoutine = new();

    private int _mouseCursorHidden;
    private MouseRegisters? _savedRegisters;
    private MouseUserCallback _userCallback;
    private int _screenHeight;
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
        IVgaFunctionality vgaFunctions, ILogger loggerService,
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
        CountButtonEvent(_buttonsReleaseCounts, e.Button);
    }

    private void OnMouseButtonPressed(object? sender, MouseButtonEventArgs e) {
        CountButtonEvent(_buttonsPressCounts, e.Button);
    }

    private void CountButtonEvent(Dictionary<MouseButton, MouseButtonCounterState> counters, MouseButton button) {
        if (!counters.TryGetValue(button, out MouseButtonCounterState entry)) {
            return;
        }

        counters[button] = new MouseButtonCounterState(entry.Count + 1, _mouseDevice.MouseXRelative,
            _mouseDevice.MouseYRelative);
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
        bool callUserRoutine = CanCallUserRoutine();
        _passCalledUserRoutine.Push(callUserRoutine);
        if (callUserRoutine) {
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
        if (_userCallback.Segment == 0 && _userCallback.Offset == 0) {
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
        if (_logger.IsEnabled(LogLevel.Trace)) {
            _logger.LogTrace("{ClassName} {MethodName}: calling {Segment:X4}:{Offset:X4} with AX={AX:X4}, BX={BX:X4}, CX={CX:X4}, DX={DX:X4}, SI={SI:X4}, DI={DI:X4}",
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
        if (!(callbackInfo.Segment == 0 && callbackInfo.Offset == 0)) {
            _sharedMouseData.UserSubroutineEverRegistered = true;
        }
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
        return _buttonsReleaseCounts.GetValueOrDefault(button).LastX;
    }

    /// <inheritdoc />
    public double GetLastReleasedY(MouseButton button) {
        return _buttonsReleaseCounts.GetValueOrDefault(button).LastY;
    }

    /// <inheritdoc />
    public int GetButtonsReleaseCount(MouseButton button) {
        return ReadAndResetCount(_buttonsReleaseCounts, button);
    }

    /// <inheritdoc />
    public int GetButtonPressCount(MouseButton button) {
        return ReadAndResetCount(_buttonsPressCounts, button);
    }

    private static int ReadAndResetCount(Dictionary<MouseButton, MouseButtonCounterState> counters,
        MouseButton button) {
        if (!counters.TryGetValue(button, out MouseButtonCounterState entry)) {
            return 0;
        }

        counters[button] = entry with { Count = 0 };
        return entry.Count;
    }

    /// <inheritdoc />
    public double GetLastPressedX(MouseButton button) {
        return _buttonsPressCounts.GetValueOrDefault(button).LastX;
    }

    /// <inheritdoc />
    public double GetLastPressedY(MouseButton button) {
        return _buttonsPressCounts.GetValueOrDefault(button).LastY;
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
        // A pass that did not call the user routine saved nothing. When it is nested inside the pass that
        // did, the saved registers and the in-progress flag belong to that outer pass and must stay intact.
        if (!_passCalledUserRoutine.TryPop(out bool calledUserRoutine) || !calledUserRoutine) {
            return;
        }
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
        double deltaYPixels = _mouseDevice.DeltaY * _screenHeight;
        double deltaYMickeys = _mouseDevice.VerticalMickeysPerPixel * deltaYPixels;
        return (short)deltaYMickeys;
    }

    /// <inheritdoc />
    public void ResetDeltaMickeys() {
        _mouseDevice.ResetDeltas();
    }

    /// <inheritdoc />
    public void Reset() {
        _screenHeight = _vgaFunctions.GetCurrentMode().Height;
        _mouseCursorHidden = -1;
        _gui?.HideMouseCursor();
        CurrentMinX = 0;
        CurrentMinY = 0;
        CurrentMaxX = VirtualScreenWidth - 1;
        CurrentMaxY = _screenHeight - 1;
        SetCursorPosition(VirtualScreenWidth / 2, _screenHeight / 2);
        HorizontalMickeysPerPixel = 8;
        VerticalMickeysPerPixel = 16;
        DoubleSpeedThreshold = 64;

        RestoreCounters(_buttonsPressCounts, default);
        RestoreCounters(_buttonsReleaseCounts, default);
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
        if (_savedRegisters is not { } savedRegisters) {
            return;
        }
        _state.ES = savedRegisters.Es;
        _state.DS = savedRegisters.Ds;
        _state.DI = savedRegisters.Di;
        _state.SI = savedRegisters.Si;
        _state.BP = savedRegisters.Bp;
        _state.SP = savedRegisters.Sp;
        _state.BX = savedRegisters.Bx;
        _state.DX = savedRegisters.Dx;
        _state.CX = savedRegisters.Cx;
        _state.AX = savedRegisters.Ax;
        // Make sure we cant restore them twice.
        // This could happen when a program disables the mouse via event mask. In this case, save would not be called and we would create a mess by restoring old values unrelated to the call.
        _savedRegisters = null;
    }

    /// <summary>
    ///     Captures the state of the mouse driver.
    /// </summary>
    /// <returns>The state of the driver.</returns>
    public MouseDriverState CaptureState() {
        // ToArray lists a stack from the top, which is the innermost pass.
        bool[] passesInProgress = _passCalledUserRoutine.ToArray();
        Array.Reverse(passesInProgress);
        return new MouseDriverState(
            CaptureCounters(_buttonsPressCounts),
            CaptureCounters(_buttonsReleaseCounts),
            _mouseCursorHidden,
            _userCallback,
            _screenHeight,
            CurrentMinX,
            CurrentMaxX,
            CurrentMinY,
            CurrentMaxY,
            _userHandlerIsBeingCalled,
            _savedRegisters,
            ImmutableArray.Create(passesInProgress));
    }

    /// <summary>
    ///     Puts the mouse driver back in a captured state.
    /// </summary>
    /// <remarks>
    ///     When the cursor visibility differs from the one before the call, the GUI is told to show or hide the
    ///     cursor. The flag the GUI keeps about whether a user routine was ever registered is left as it is.
    /// </remarks>
    /// <param name="state">State captured by <see cref="CaptureState" />.</param>
    public void RestoreState(MouseDriverState state) {
        bool wasCursorShown = _mouseCursorHidden == 0;
        RestoreCounters(_buttonsPressCounts, state.ButtonPresses);
        RestoreCounters(_buttonsReleaseCounts, state.ButtonReleases);
        _mouseCursorHidden = state.CursorHiddenCounter;
        _userCallback = state.UserCallback;
        _screenHeight = state.ScreenHeight;
        CurrentMinX = state.MinX;
        CurrentMaxX = state.MaxX;
        CurrentMinY = state.MinY;
        CurrentMaxY = state.MaxY;
        _userHandlerIsBeingCalled = state.IsUserRoutineRunning;
        _savedRegisters = state.SavedRegisters;
        _passCalledUserRoutine.Clear();
        foreach (bool calledUserRoutine in state.PassesInProgress) {
            _passCalledUserRoutine.Push(calledUserRoutine);
        }

        bool isCursorShown = _mouseCursorHidden == 0;
        if (isCursorShown && !wasCursorShown) {
            _gui?.ShowMouseCursor();
        } else if (!isCursorShown && wasCursorShown) {
            _gui?.HideMouseCursor();
        }
    }

    private static MouseButtonCountersState CaptureCounters(
        Dictionary<MouseButton, MouseButtonCounterState> counters) {
        return new MouseButtonCountersState(counters[MouseButton.Left], counters[MouseButton.Right],
            counters[MouseButton.Middle]);
    }

    private static void RestoreCounters(Dictionary<MouseButton, MouseButtonCounterState> counters,
        MouseButtonCountersState state) {
        counters[MouseButton.Left] = state.Left;
        counters[MouseButton.Right] = state.Right;
        counters[MouseButton.Middle] = state.Middle;
    }

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
        memoryAsmWriter.RegisterAndWriteCallback(CallbackNumbers.MouseBeforeUserHandler, BeforeUserHandlerExecution);
        // Far call to default handler, can be changed via _inMemoryAddressSwitcher
        memoryAsmWriter.WriteFarCallToSwitcherDefaultAddress(_userHandlerAddressSwitcher);
        memoryAsmWriter.RegisterAndWriteCallback(CallbackNumbers.MouseAfterUserHandler, AfterUserHandlerExecution);
        // Far ret to return to caller
        memoryAsmWriter.WriteFarRet();
        return driverAddress;
    }
}
