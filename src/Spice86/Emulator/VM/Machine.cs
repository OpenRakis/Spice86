namespace Spice86.Emulator.VM;

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
using Spice86.UI.ViewModels;

using System;
using System.Collections.Generic;

/// <summary>
/// Emulates an IBM PC
/// </summary>
public class Machine : IDisposable {
    private const int InterruptHandlersSegment = 0xF000;
    private ProgramExecutor _programExecutor;

    public Machine(ProgramExecutor programExecutor, MainWindowViewModel? gui, CounterConfigurator counterConfigurator, JumpHandler jumpHandler, Configuration configuration, bool debugMode) {
        _programExecutor = programExecutor;
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

        Pic = new Pic(this, configuration);
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
        OPL3FM = new OPL3FM(this, configuration);
        Register(OPL3FM);
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

    public bool DebugMode { get; }

    public string DumpCallStack() {
        FunctionHandler inUse = Cpu.FunctionHandlerInUse;
        string callStack = "";
        if (inUse.Equals(Cpu.FunctionHandlerInExternalInterrupt)) {
            callStack += "From external interrupt:\n";
        }

        callStack += inUse.DumpCallStack();
        return callStack;
    }

    public BiosEquipmentDeterminationInt11Handler BiosEquipmentDeterminationInt11Handler { get; }

    public BiosKeyboardInt9Handler BiosKeyboardInt9Handler { get; }

    public CallbackHandler CallbackHandler { get; }

    public Cpu Cpu { get; }

    public DosInt20Handler DosInt20Handler { get; }

    public DosInt21Handler DosInt21Handler { get; }

    public GravisUltraSound GravisUltraSound { get; }

    public MainWindowViewModel? Gui { get; }

    public IOPortDispatcher IoPortDispatcher { get; }

    public Joystick Joystick { get; }

    public Keyboard Keyboard { get; }

    public KeyboardInt16Handler KeyboardInt16Handler { get; }

    public MachineBreakpoints MachineBreakpoints { get; }

    public Memory Memory { get; }

    public Midi Midi { get; }

    public MouseInt33Handler MouseInt33Handler { get; }

    public PcSpeaker PcSpeaker { get; }

    public Pic Pic { get; }

    public SoundBlaster SoundBlaster { get; }

    public SystemBiosInt15Handler SystemBiosInt15Handler { get; }

    public SystemClockInt1AHandler SystemClockInt1AHandler { get; }

    public Timer Timer { get; }

    public TimerInt8Handler TimerInt8Handler { get; }

    public VgaCard VgaCard { get; }

    public VideoBiosInt10Handler VideoBiosInt10Handler { get; }
    public DmaController DmaController { get; }
    /// <summary>
    /// Gets the current DOS environment variables.
    /// TODO: Make use of it by allocating the block of memory corresponding to it in virtual memory.
    /// </summary>
    public EnvironmentVariables EnvironmentVariables { get; } = new EnvironmentVariables();
    public OPL3FM OPL3FM { get; }

    public event Action? Paused;

    public event Action? Resumed;

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

        if (ioPortHandler is IDmaDevice8 dmaDevice) {
            if (dmaDevice.Channel < 0 || dmaDevice.Channel >= DmaController.Channels.Count) {
                throw new ArgumentException("Invalid DMA channel on DMA device.");
            }

            DmaController.Channels[dmaDevice.Channel].Device = dmaDevice;
            dmaDeviceChannels.Add(DmaController.Channels[dmaDevice.Channel]);
        }
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
                Paused?.Invoke();
                if (_programExecutor.Step() == false) {
                    Gui?.WaitOne();
                }
                Resumed?.Invoke();
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
            if (channel.IsActive && !channel.IsMasked) {
                channel.Transfer(this.Memory);
            }
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