namespace Spice86.Emulator.VM;

using Serilog;

using Spice86.Emulator.Callback;
using Spice86.Emulator.CPU;
using Spice86.Emulator.Devices.ExternalInput;
using Spice86.Emulator.Devices.Input.Joystick;
using Spice86.Emulator.Devices.Input.Keyboard;
using Spice86.Emulator.Devices.Sound;
using Spice86.Emulator.Devices.Timer;
using Spice86.Emulator.Devices.Video;
using Spice86.Emulator.Errors;
using Spice86.Emulator.Function;
using Spice86.Emulator.InterruptHandlers.Bios;
using Spice86.Emulator.InterruptHandlers.Dos;
using Spice86.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Emulator.InterruptHandlers.SystemClock;
using Spice86.Emulator.InterruptHandlers.Timer;
using Spice86.Emulator.InterruptHandlers.Vga;
using Spice86.Emulator.IOPorts;
using Spice86.Emulator.Memory;
using Spice86.UI;

using System;

/// <summary>
/// Emulates an IBM PC
/// </summary>
public class Machine {
    private const int InterruptHandlersSegment = 0xF000;
    private readonly BiosEquipmentDeterminationInt11Handler _biosEquipmentDeterminationInt11Handler;
    private readonly BiosKeyboardInt9Handler _biosKeyboardInt9Handler;
    private readonly CallbackHandler _callbackHandler;
    private readonly Cpu _cpu;
    private readonly bool _debugMode;
    private readonly DosInt20Handler _dosInt20Handler;
    private readonly DosInt21Handler _dosInt21Handler;
    private readonly GravisUltraSound _gravisUltraSound;
    private readonly IVideoKeyboardMouseIO? _gui;

    // IO Devices
    private readonly IOPortDispatcher _ioPortDispatcher;

    private readonly Joystick _joystick;
    private readonly Keyboard _keyboard;
    private readonly KeyboardInt16Handler _keyboardInt16Handler;
    private readonly MachineBreakpoints _machineBreakpoints;
    private readonly Memory _memory;
    private readonly Midi _midi;
    private readonly MouseInt33Handler _mouseInt33Handler;
    private readonly PcSpeaker _pcSpeaker;
    private readonly Pic _pic;
    private readonly SoundBlaster _soundBlaster;
    private readonly SystemBiosInt15Handler _systemBiosInt15Handler;
    private readonly SystemClockInt1AHandler _systemClockInt1AHandler;
    private readonly Timer _timer;

    // Services for int callbacks
    private readonly TimerInt8Handler _timerInt8Handler;

    private readonly VgaCard _vgaCard;
    private readonly VideoBiosInt10Handler _videoBiosInt10Handler;

    public Machine(IVideoKeyboardMouseIO? gui, CounterConfigurator counterConfigurator, JumpHandler jumpHandler, bool failOnUnhandledPort, bool debugMode) {
        _gui = gui;
        _debugMode = debugMode;

        // A full 1MB of addressable memory :)
        _memory = new Memory(0x100000);
        _cpu = new Cpu(this, jumpHandler, debugMode);

        // Breakpoints
        _machineBreakpoints = new MachineBreakpoints(this);

        // IO devices
        _ioPortDispatcher = new IOPortDispatcher(this, failOnUnhandledPort);
        _cpu.SetIoPortDispatcher(_ioPortDispatcher);
        _pic = new Pic(this, true, failOnUnhandledPort);
        Register(_pic);
        _vgaCard = new VgaCard(this, gui, failOnUnhandledPort);
        Register(_vgaCard);
        _timer = new Timer(this, _pic, _vgaCard, counterConfigurator, failOnUnhandledPort);
        Register(_timer);
        _keyboard = new Keyboard(this, gui, failOnUnhandledPort);
        Register(_keyboard);
        _joystick = new Joystick(this, failOnUnhandledPort);
        Register(_joystick);
        _pcSpeaker = new PcSpeaker(this, failOnUnhandledPort);
        Register(_pcSpeaker);
        _soundBlaster = new SoundBlaster(this, failOnUnhandledPort);
        Register(_soundBlaster);
        _gravisUltraSound = new GravisUltraSound(this, failOnUnhandledPort);
        Register(_gravisUltraSound);
        _midi = new Midi(this, failOnUnhandledPort);
        Register(_midi);

        // Services
        _callbackHandler = new CallbackHandler(this, (ushort)InterruptHandlersSegment);
        _cpu.SetCallbackHandler(_callbackHandler);
        _timerInt8Handler = new TimerInt8Handler(this);
        Register(_timerInt8Handler);
        _biosKeyboardInt9Handler = new BiosKeyboardInt9Handler(this);
        Register(_biosKeyboardInt9Handler);
        _videoBiosInt10Handler = new VideoBiosInt10Handler(this, _vgaCard);
        _videoBiosInt10Handler.InitRam();
        Register(_videoBiosInt10Handler);
        _biosEquipmentDeterminationInt11Handler = new BiosEquipmentDeterminationInt11Handler(this);
        Register(_biosEquipmentDeterminationInt11Handler);
        _systemBiosInt15Handler = new SystemBiosInt15Handler(this);
        Register(_systemBiosInt15Handler);
        _keyboardInt16Handler = new KeyboardInt16Handler(this, _biosKeyboardInt9Handler.GetBiosKeyboardBuffer());
        Register(_keyboardInt16Handler);
        _systemClockInt1AHandler = new SystemClockInt1AHandler(this, _timerInt8Handler);
        Register(_systemClockInt1AHandler);
        _dosInt20Handler = new DosInt20Handler(this);
        Register(_dosInt20Handler);
        _dosInt21Handler = new DosInt21Handler(this);
        Register(_dosInt21Handler);
        _mouseInt33Handler = new MouseInt33Handler(this, gui);
        Register(_mouseInt33Handler);
    }

    public string DumpCallStack() {
        FunctionHandler inUse = _cpu.GetFunctionHandlerInUse();
        string callStack = "";
        if (inUse.Equals(_cpu.GetFunctionHandlerInExternalInterrupt())) {
            callStack += "From external interrupt:\n";
        }

        callStack += inUse.DumpCallStack();
        return callStack;
    }

    public BiosEquipmentDeterminationInt11Handler GetBiosEquipmentDeterminationInt11Handler() {
        return _biosEquipmentDeterminationInt11Handler;
    }

    public BiosKeyboardInt9Handler GetBiosKeyboardInt9Handler() {
        return _biosKeyboardInt9Handler;
    }

    public CallbackHandler GetCallbackHandler() {
        return _callbackHandler;
    }

    public Cpu GetCpu() {
        return _cpu;
    }

    public DosInt20Handler GetDosInt20Handler() {
        return _dosInt20Handler;
    }

    public DosInt21Handler GetDosInt21Handler() {
        return _dosInt21Handler;
    }

    public GravisUltraSound GetGravisUltraSound() {
        return _gravisUltraSound;
    }

    public IVideoKeyboardMouseIO? GetGui() {
        return _gui;
    }

    public IOPortDispatcher GetIoPortDispatcher() {
        return _ioPortDispatcher;
    }

    public Joystick GetJoystick() {
        return _joystick;
    }

    public Keyboard GetKeyboard() {
        return _keyboard;
    }

    public KeyboardInt16Handler GetKeyboardInt16Handler() {
        return _keyboardInt16Handler;
    }

    public MachineBreakpoints GetMachineBreakpoints() {
        return _machineBreakpoints;
    }

    public Memory GetMemory() {
        return _memory;
    }

    public Midi GetMidi() {
        return _midi;
    }

    public MouseInt33Handler GetMouseInt33Handler() {
        return _mouseInt33Handler;
    }

    public PcSpeaker GetPcSpeaker() {
        return _pcSpeaker;
    }

    public Pic GetPic() {
        return _pic;
    }

    public SoundBlaster GetSoundBlaster() {
        return _soundBlaster;
    }

    public SystemBiosInt15Handler GetSystemBiosInt15Handler() {
        return _systemBiosInt15Handler;
    }

    public SystemClockInt1AHandler GetSystemClockInt1AHandler() {
        return _systemClockInt1AHandler;
    }

    public Timer GetTimer() {
        return _timer;
    }

    public TimerInt8Handler GetTimerInt8Handler() {
        return _timerInt8Handler;
    }

    public VgaCard GetVgaCard() {
        return _vgaCard;
    }

    public VideoBiosInt10Handler GetVideoBiosInt10Handler() {
        return _videoBiosInt10Handler;
    }

    public void InstallAllCallbacksInInterruptTable() {
        _callbackHandler.InstallAllCallbacksInInterruptTable();
    }

    public string PeekReturn() {
        return ToString(_cpu.GetFunctionHandlerInUse().PeekReturnAddressOnMachineStackForCurrentFunction());
    }

    public string PeekReturn(CallType returnCallType) {
        return ToString(_cpu.GetFunctionHandlerInUse().PeekReturnAddressOnMachineStack(returnCallType));
    }

    public void Register(IIOPortHandler ioPortHandler) {
        ioPortHandler.InitPortHandlers(_ioPortDispatcher);
    }

    public void Register(ICallback callback) {
        _callbackHandler.AddCallback(callback);
    }

    public void Run() {
        State state = _cpu.GetState();
        FunctionHandler functionHandler = _cpu.GetFunctionHandler();
        functionHandler.Call(CallType.MACHINE, state.GetCS(), state.GetIP(), null, null, () => "entry", false);
        try {
            RunLoop();
        } catch (InvalidVMOperationException) {
            throw;
        } catch (Exception e) {
            throw new InvalidVMOperationException(this, e);
        }

        _machineBreakpoints.OnMachineStop();
        functionHandler.Ret(CallType.MACHINE);
    }

    private void RunLoop() {
        while (_cpu.IsRunning) {
            if (_debugMode) {
                _machineBreakpoints.CheckBreakPoint();
            }

            _cpu.ExecuteNextInstruction();
            _timer.Tick();
        }
    }

    private static string ToString(SegmentedAddress? segmentedAddress) {
        if (segmentedAddress is not null) {
            return segmentedAddress.ToString();
        }

        return "null";
    }
}