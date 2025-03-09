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
        { MouseButton.Left, new() },
        { MouseButton.Right, new() },
        { MouseButton.Middle, new() }
    };


    private const byte BeforeUserHandlerExecutionCallbackNumber = 0xFE;
    private const byte AfterUserHandlerExecutionCallbackNumber = 0xFF;
    private const int VirtualScreenWidth = 640;
    private readonly IGui? _gui;
    private readonly ILoggerService _logger;
    private readonly IMouseDevice _mouseDevice;
    private readonly State _state;

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
    /// <param name="cpu">Cpu instance to use for calling functions and saving/restoring registers</param>
    /// <param name="memory">Memory instance to look into the interrupt vector table</param>
    /// <param name="mouseDevice">The mouse device / hardware</param>
    /// <param name="gui">The gui to show, hide and position mouse cursor</param>
    /// <param name="vgaFunctions">Access to the current resolution</param>
    /// <param name="loggerService">The logger</param>
    public MouseDriver(Cpu cpu, IIndexable memory, IMouseDevice mouseDevice, IGui? gui, IVgaFunctionality vgaFunctions, ILoggerService loggerService) {
        _state = cpu.State;
        _logger = loggerService;
        _mouseDevice = mouseDevice;
        _gui = gui;
        if (_gui is not null) {
            _gui.MouseButtonUp += OnMouseButtonUp;
        }
        _vgaFunctions = vgaFunctions;

        _vgaFunctions.VideoModeChanged += OnVideoModeChanged;
        _userHandlerAddressSwitcher = new(memory);
        Reset();
    }
    private void OnMouseButtonUp(object? sender, MouseButtonEventArgs e) {
        MouseButton button = e.Button switch {
            MouseButton.Left => MouseButton.Left,
            MouseButton.Right => MouseButton.Right,
            MouseButton.Middle => MouseButton.Middle,
            _ => throw new ArgumentOutOfRangeException(nameof(e))
        };

        _buttonsPressCounts[button].PressCount++;
        _buttonsPressCounts[button].LastPressedX = _mouseDevice.MouseXRelative;
        _buttonsPressCounts[button].LastPressedY = _mouseDevice.MouseYRelative;
    }

    /// <inheritdoc />
    public int CurrentMinX { get; set; }

    /// <inheritdoc />
    public int CurrentMaxX { get; set; }

    /// <inheritdoc />
    public int CurrentMaxY { get; set; }

    /// <inheritdoc />
    public int CurrentMinY { get; set; }

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
        if (_userHandlerIsBeingCalled) {
            // Call already in progress
            return false;
        }
        return true;
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
        _mouseDevice.MouseXRelative = (double)x / mouseAreaWidth;
        _mouseDevice.MouseYRelative = (double)y / mouseAreaHeight;
        // This does not do anything in Avalonia, but it could in a different UI.
        if (_gui != null) {
            _gui.MouseX = x;
            _gui.MouseY = y;
        }
    }

    /// <inheritdoc />
    public int ButtonCount => _mouseDevice.ButtonCount;

    /// <inheritdoc />
    public MouseType MouseType => _mouseDevice.MouseType;

    /// <inheritdoc />
    public MouseStatus GetCurrentMouseStatus() {
        int x = LinearInterpolate(_mouseDevice.MouseXRelative, CurrentMinX, CurrentMaxX);
        int y = LinearInterpolate(_mouseDevice.MouseYRelative, CurrentMinY, CurrentMaxY);
        ushort buttonFlags = (ushort)((_mouseDevice.IsLeftButtonDown ? 1 : 0) | (_mouseDevice.IsRightButtonDown ? 2 : 0) | (_mouseDevice.IsMiddleButtonDown ? 4 : 0));
        return new MouseStatus(x, y, buttonFlags);
    }

    /// <inheritdoc />
    public int GetButtonPressCount(MouseButton button) {
        MouseButton mouseButton = (MouseButton)button;
        int count = _buttonsPressCounts.TryGetValue(mouseButton,
            out MouseButtonPressCount? value) ? value.PressCount : 0;

        // Reset the count after reading
        _buttonsPressCounts[mouseButton].PressCount = 0;

        return count;
    }

    /// <inheritdoc />
    public double GetLastPressedX(MouseButton button) {
        MouseButton mouseButton = (MouseButton)button;
        return _buttonsPressCounts.TryGetValue(mouseButton,
            out MouseButtonPressCount? position) ? position.LastPressedX : 0;
    }

    /// <inheritdoc />
    public double GetLastPressedY(MouseButton button) {
        MouseButton mouseButton = (MouseButton)button;
        return _buttonsPressCounts.TryGetValue(mouseButton,
            out MouseButtonPressCount? position) ? position.LastPressedX : 0;
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

        foreach (MouseButton button in _buttonsPressCounts.Keys) {
            _buttonsPressCounts[button].PressCount = 0;
            _buttonsPressCounts[button].LastPressedX = 0;
            _buttonsPressCounts[button].LastPressedY = 0;
        }
    }

    private void OnVideoModeChanged(object? sender, VideoModeChangedEventArgs e) {
        Reset();
    }

    private static int LinearInterpolate(double index, int min, int max) {
        return (int)(min + (max - min) * index);
    }

    private void SetRegistersToMouseState() {
        // Set mouse info
        MouseStatus status = GetCurrentMouseStatus();
        _state.AX = (ushort)_mouseDevice.LastTrigger;
        _state.BX = (ushort)status.ButtonFlags;
        _state.CX = (ushort)status.X;
        _state.DX = (ushort)status.Y;
        _state.SI = (ushort)GetDeltaXMickeys();
        _state.DI = (ushort)GetDeltaYMickeys();
    }

    private void SaveRegisters() {
        _savedRegisters = new MouseRegisters(_state.ES, _state.DS, _state.DI, _state.SI, _state.BP, _state.SP, _state.BX, _state.DX, _state.CX, _state.AX);
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
        memoryAsmWriter.RegisterAndWriteCallback(BeforeUserHandlerExecutionCallbackNumber, BeforeUserHandlerExecution);
        // Far call to default handler, can be changed via _inMemoryAddressSwitcher
        memoryAsmWriter.WriteFarCallToSwitcherDefaultAddress(_userHandlerAddressSwitcher);
        memoryAsmWriter.RegisterAndWriteCallback(AfterUserHandlerExecutionCallbackNumber, AfterUserHandlerExecution);
        // Far ret to return to caller
        memoryAsmWriter.WriteFarRet();
        return driverAddress;
    }
}