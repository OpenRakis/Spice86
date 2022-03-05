namespace Spice86.Emulator.VM;

using Serilog;

using Spice86.Emulator.Callback;
using Spice86.Emulator.CPU;
using Spice86.Emulator.Devices.DirectMemoryAccess;
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
using System.Collections.Generic;

/// <summary>
/// Emulates an IBM PC
/// </summary>
public class Machine : IDisposable {
    private const int InterruptHandlersSegment = 0xF000;
    private readonly Configuration _configuration;

    public Machine(IGraphicalUserInterface? gui, CounterConfigurator counterConfigurator, JumpHandler jumpHandler, Configuration configuration, bool debugMode) {
        _configuration = configuration;
        Gui = gui;
        DebugMode = debugMode;

        // A full 1MB of addressable memory :)
        Memory = new Memory(0x100000);
        Cpu = new Cpu(this, jumpHandler, debugMode);

        // Breakpoints
        MachineBreakpoints = new MachineBreakpoints(this);

        // IO devices
        IoPortDispatcher = new IOPortDispatcher(this, configuration);
        Cpu.IoPortDispatcher = IoPortDispatcher;

        this.DmaController = new DmaController(this, configuration);
        Register(DmaController);

        Pic = new Pic(this, true, configuration);
        Register(Pic);
        VgaCard = new VgaCard(this, gui, configuration);
        Register(VgaCard);
        Timer = new Timer(this, Pic, VgaCard, counterConfigurator, configuration);
        Register(Timer);
        Keyboard = new Keyboard(this, gui, configuration);
        Register(Keyboard);
        Joystick = new Joystick(this, configuration);
        Register(Joystick);
        PcSpeaker = new PcSpeaker(this, configuration);
        Register(PcSpeaker);
        SoundBlaster = new SoundBlaster(this, configuration);
        Register(SoundBlaster);
        SoundBlaster.AddEnvironnmentVariable();
        GravisUltraSound = new GravisUltraSound(this, configuration);
        Register(GravisUltraSound);
        Midi = new Midi(this, configuration);
        Register(Midi);

        // Services
        CallbackHandler = new CallbackHandler(this, (ushort)InterruptHandlersSegment);
        Cpu.CallbackHandler = CallbackHandler;
        TimerInt8Handler = new TimerInt8Handler(this);
        Register(TimerInt8Handler);
        BiosKeyboardInt9Handler = new BiosKeyboardInt9Handler(this);
        Register(BiosKeyboardInt9Handler);
        VideoBiosInt10Handler = new VideoBiosInt10Handler(this, VgaCard);
        VideoBiosInt10Handler.InitRam();
        Register(VideoBiosInt10Handler);
        BiosEquipmentDeterminationInt11Handler = new BiosEquipmentDeterminationInt11Handler(this);
        Register(BiosEquipmentDeterminationInt11Handler);
        SystemBiosInt15Handler = new SystemBiosInt15Handler(this);
        Register(SystemBiosInt15Handler);
        KeyboardInt16Handler = new KeyboardInt16Handler(this, BiosKeyboardInt9Handler.BiosKeyboardBuffer);
        Register(KeyboardInt16Handler);
        SystemClockInt1AHandler = new SystemClockInt1AHandler(this, TimerInt8Handler);
        Register(SystemClockInt1AHandler);
        DosInt20Handler = new DosInt20Handler(this);
        Register(DosInt20Handler);
        DosInt21Handler = new DosInt21Handler(this);
        Register(DosInt21Handler);
        MouseInt33Handler = new MouseInt33Handler(this, gui);
        Register(MouseInt33Handler);
    }

    public DosMemoryManager DosMemoryManager => DosInt21Handler.DosMemoryManager;

    public bool DebugMode { get; private set; }

    public string DumpCallStack() {
        FunctionHandler inUse = Cpu.FunctionHandlerInUse;
        string callStack = "";
        if (inUse.Equals(Cpu.FunctionHandlerInExternalInterrupt)) {
            callStack += "From external interrupt:\n";
        }

        callStack += inUse.DumpCallStack();
        return callStack;
    }

    public BiosEquipmentDeterminationInt11Handler BiosEquipmentDeterminationInt11Handler { get; private set; }

    public BiosKeyboardInt9Handler BiosKeyboardInt9Handler { get; private set; }

    public CallbackHandler CallbackHandler { get; private set; }

    public Cpu Cpu { get; private set; }

    public DosInt20Handler DosInt20Handler { get; private set; }

    public DosInt21Handler DosInt21Handler { get; private set; }

    public GravisUltraSound GravisUltraSound { get; private set; }

    public IGraphicalUserInterface? Gui { get; private set; }

    public IOPortDispatcher IoPortDispatcher { get; private set; }

    public Joystick Joystick { get; private set; }

    public Keyboard Keyboard { get; private set; }

    public KeyboardInt16Handler KeyboardInt16Handler { get; private set; }

    public MachineBreakpoints MachineBreakpoints { get; private set; }

    public Memory Memory { get; private set; }

    public Midi Midi { get; private set; }

    public MouseInt33Handler MouseInt33Handler { get; private set; }

    public PcSpeaker PcSpeaker { get; private set; }

    public Pic Pic { get; private set; }

    public SoundBlaster SoundBlaster { get; private set; }

    public SystemBiosInt15Handler SystemBiosInt15Handler { get; private set; }

    public SystemClockInt1AHandler SystemClockInt1AHandler { get; private set; }

    public Timer Timer { get; private set; }

    public TimerInt8Handler TimerInt8Handler { get; private set; }

    public VgaCard VgaCard { get; private set; }

    public VideoBiosInt10Handler VideoBiosInt10Handler { get; private set; }
    public DmaController DmaController { get; private set; }
    /// <summary>
    /// Gets the current DOS environment variables.
    /// TODO: Make use of it by allocating the block of memory corresponding to it in virtual memory.
    /// </summary>
    public EnvironmentVariables EnvironmentVariables { get; } = new EnvironmentVariables();

    public event EventHandler? Paused;

    public event EventHandler? Resumed;

    public void InstallAllCallbacksInInterruptTable() {
        CallbackHandler.InstallAllCallbacksInInterruptTable();
    }

    public string PeekReturn() {
        return ToString(Cpu.FunctionHandlerInUse.PeekReturnAddressOnMachineStackForCurrentFunction());
    }

    public string PeekReturn(CallType returnCallType) {
        return ToString(Cpu.FunctionHandlerInUse.PeekReturnAddressOnMachineStack(returnCallType));
    }

    public void Register(IIOPortHandler ioPortHandler) {
        ioPortHandler.InitPortHandlers(IoPortDispatcher);
    }

    public void Register(ICallback callback) {
        CallbackHandler.AddCallback(callback);
    }

    public void Run() {
        State state = Cpu.State;
        FunctionHandler functionHandler = Cpu.FunctionHandler;
        functionHandler.Call(CallType.MACHINE, state.CS, state.IP, null, null, "entry", false);
        try {
            RunLoop();
        } catch (InvalidVMOperationException) {
            throw;
        } catch (Exception e) {
            throw new InvalidVMOperationException(this, e);
        }

        MachineBreakpoints.OnMachineStop();
        functionHandler.Ret(CallType.MACHINE);
    }

    private void RunLoop() {
        while (Cpu.IsRunning) {
            if (Gui?.IsPaused == true) {
                Paused?.Invoke(this, EventArgs.Empty);
                Gui?.WaitOne();
                Resumed?.Invoke(this, EventArgs.Empty);
            }
            if (DebugMode) {
                MachineBreakpoints.CheckBreakPoint();
            }

            Cpu.ExecuteNextInstruction();
            Timer.Tick();
        }
    }

    private static string ToString(SegmentedAddress? segmentedAddress) {
        if (segmentedAddress is not null) {
            return segmentedAddress.ToString();
        }

        return "null";
    }

    private readonly List<DmaChannel> dmaDeviceChannels = new();
    private bool disposedValue;

    internal void PerformDmaTransfers() {
        foreach (DmaChannel? channel in this.dmaDeviceChannels) {
            if (channel.IsActive && !channel.IsMasked)
                channel.Transfer(this.Memory);
        }
    }

    protected virtual void Dispose(bool disposing) {
        if (!disposedValue) {
            if (disposing) {
                SoundBlaster.Dispose();
            }
            disposedValue = true;
        }
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}