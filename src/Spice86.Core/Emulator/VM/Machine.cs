using Spice86.Core.Emulator.Devices.Memory;

namespace Spice86.Core.Emulator.VM;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.SystemClock;
using Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

/// <summary>
/// Emulates an IBM PC
/// </summary>
public class Machine : IDisposable {
    private readonly ProgramExecutor _programExecutor;
    private readonly List<DmaChannel> _dmaDeviceChannels = new();
    private readonly Thread _dmaThread;
    private bool _exitDmaLoop = false;
    private bool _dmaThreadStarted = false;
    private readonly ManualResetEvent _dmaResetEvent = new(true);

    private bool _disposed;

    public bool RecordData { get; set; }
    
    public Bios Bios { get; set; }

    public BiosEquipmentDeterminationInt11Handler BiosEquipmentDeterminationInt11Handler { get; }

    public BiosKeyboardInt9Handler BiosKeyboardInt9Handler { get; }

    public CallbackHandler CallbackHandler { get; }

    public Cpu Cpu { get; }

    public Dos Dos { get; }

    public GravisUltraSound GravisUltraSound { get; }

    public IGui? Gui { get; }

    public IOPortDispatcher IoPortDispatcher { get; }

    public Joystick Joystick { get; }

    public Keyboard Keyboard { get; }

    public KeyboardInt16Handler KeyboardInt16Handler { get; }

    public MachineBreakpoints MachineBreakpoints { get; }

    public Memory Memory { get; }

    public Midi Midi { get; }

    public MouseInt33Handler MouseInt33Handler { get; }

    public PcSpeaker PcSpeaker { get; }

    public DualPic DualPic { get; }

    public SoundBlaster SoundBlaster { get; }

    public SystemBiosInt15Handler SystemBiosInt15Handler { get; }

    public SystemClockInt1AHandler SystemClockInt1AHandler { get; }

    public Timer Timer { get; }

    public TimerInt8Handler TimerInt8Handler { get; }

    public IVideoCard VgaCard { get; }

    public DmaController DmaController { get; }

    public ExpandedMemoryManager? Ems { get; }

    /// <summary>
    /// Gets the current DOS environment variables.
    /// </summary>
    public EnvironmentVariables EnvironmentVariables { get; } = new EnvironmentVariables();

    public OPL3FM OPL3FM { get; }
    
    public EmsCard EmsCard { get; }

    public event Action? Paused;

    public event Action? Resumed;

    public Configuration Configuration { get; }

    public Machine(ProgramExecutor programExecutor, IGui? gui, IKeyScanCodeConverter? keyScanCodeConverter, ILoggerService loggerService, CounterConfigurator counterConfigurator, ExecutionFlowRecorder executionFlowRecorder, Configuration configuration, bool recordData) {
        _programExecutor = programExecutor;
        Configuration = configuration;
        Gui = gui;
        RecordData = recordData;

        IMemoryDevice ram = new Ram((uint)Configuration.Kilobytes * 1024);
        Memory = new Memory(ram);
        Bios = new Bios(Memory);
        Cpu = new Cpu(this, loggerService, executionFlowRecorder, recordData);
        
        // Breakpoints
        MachineBreakpoints = new MachineBreakpoints(this, loggerService);

        // IO devices
        IoPortDispatcher = new IOPortDispatcher(
            this,
            loggerService,
            configuration);
        Cpu.IoPortDispatcher = IoPortDispatcher;

        DmaController = new DmaController(this, configuration, loggerService);
        Register(DmaController);

        DualPic = new DualPic(this, configuration, loggerService);
        Register(DualPic);
        VgaCard = new AeonCard(this, loggerService, gui, configuration);
        Register(VgaCard as IIOPortHandler ?? throw new InvalidOperationException());
        Timer = new Timer(this, loggerService, DualPic, VgaCard, counterConfigurator, configuration);
        Register(Timer);
        Keyboard = new Keyboard(this, loggerService, gui, keyScanCodeConverter, configuration);
        Register(Keyboard);
        Joystick = new Joystick(this, configuration, loggerService);
        Register(Joystick);
        PcSpeaker = new PcSpeaker(this, loggerService, configuration);
        Register(PcSpeaker);
        OPL3FM = new OPL3FM(this, configuration, loggerService);
        Register(OPL3FM);
        SoundBlaster = new SoundBlaster(this, configuration, loggerService);
        Register(SoundBlaster);
        SoundBlaster.AddEnvironnmentVariable();
        GravisUltraSound = new GravisUltraSound(this, configuration, loggerService);
        Register(GravisUltraSound);
        Midi = new Midi(this, configuration, loggerService);
        Register(Midi);

        // Services
        CallbackHandler = new CallbackHandler(this, MemoryMap.InterruptHandlersSegment);
        Cpu.CallbackHandler = CallbackHandler;
        TimerInt8Handler = new TimerInt8Handler(this);
        Register(TimerInt8Handler);
        BiosKeyboardInt9Handler = new BiosKeyboardInt9Handler(this,
            loggerService,
            keyScanCodeConverter);
        Register(BiosKeyboardInt9Handler);
        BiosEquipmentDeterminationInt11Handler = new BiosEquipmentDeterminationInt11Handler(this);
        Register(BiosEquipmentDeterminationInt11Handler);
        SystemBiosInt15Handler = new SystemBiosInt15Handler(this);
        Register(SystemBiosInt15Handler);
        KeyboardInt16Handler = new KeyboardInt16Handler(
            this,
            loggerService,
            BiosKeyboardInt9Handler.BiosKeyboardBuffer);
        Register(KeyboardInt16Handler);
        SystemClockInt1AHandler = new SystemClockInt1AHandler(
            this,
            loggerService,
            TimerInt8Handler);
        Register(SystemClockInt1AHandler);

        // Initialize DOS.
        Dos = new Dos(this, loggerService);
        Dos.Initialize();
        
        MouseInt33Handler = new MouseInt33Handler(this, loggerService, gui);
        Register(MouseInt33Handler);
        _dmaThread = new Thread(DmaLoop) {
            Name = "DMAThread"
        };
        EmsCard = new(this, configuration, loggerService);
        if(configuration.Ems) {
            Ems = new(this);
        }
        if(Ems is not null) {
            Register(Ems);
        }
    }

    public void Register(IIOPortHandler ioPortHandler) {
        ioPortHandler.InitPortHandlers(IoPortDispatcher);

        if (ioPortHandler is not IDmaDevice8 dmaDevice) {
            return;
        }

        if (dmaDevice.Channel < 0 || dmaDevice.Channel >= DmaController.Channels.Count) {
            throw new ArgumentException("Invalid DMA channel on DMA device.");
        }

        DmaController.Channels[dmaDevice.Channel].Device = dmaDevice;
        _dmaDeviceChannels.Add(DmaController.Channels[dmaDevice.Channel]);
    }

    public void Register(ICallback callback) {
        CallbackHandler.AddCallback(callback);
    }

    public void Register(IIOPortHandler ioPortHandler) {
        ioPortHandler.InitPortHandlers(IoPortDispatcher);

        if (ioPortHandler is not IDmaDevice8 dmaDevice) {
            return;
        }

        if (dmaDevice.Channel < 0 || dmaDevice.Channel >= DmaController.Channels.Count) {
            throw new ArgumentException("Invalid DMA channel on DMA device.");
        }

        DmaController.Channels[dmaDevice.Channel].Device = dmaDevice;
        _dmaDeviceChannels.Add(DmaController.Channels[dmaDevice.Channel]);
    }

    public void Register(ICallback callback) {
        CallbackHandler.AddCallback(callback);
    }

    /// <summary>
    /// https://techgenix.com/direct-memory-access/
    /// </summary>
    private void DmaLoop() {
        while (Cpu.IsRunning && !_exitDmaLoop && !_exitEmulationLoop && !_disposed) {
            for (int i = 0; i < _dmaDeviceChannels.Count; i++) {
                DmaChannel dmaChannel = _dmaDeviceChannels[i];
                if (Gui?.IsPaused == true || IsPaused) {
                    Gui?.WaitForContinue();
                }
                dmaChannel.Transfer(Memory);
                if (!_exitDmaLoop) {
                    _dmaResetEvent.WaitOne(1);
                }
            }
        }
    }

    public string DumpCallStack() {
        FunctionHandler inUse = Cpu.FunctionHandlerInUse;
        StringBuilder sb = new();
        if (inUse.Equals(Cpu.FunctionHandlerInExternalInterrupt)) {
            sb.AppendLine("From external interrupt:");
        }

        sb.Append(inUse.DumpCallStack());
        return sb.ToString();
    }

    public void InstallAllCallbacksInInterruptTable() {
        CallbackHandler.InstallAllCallbacksInInterruptTable();
    }

    public string PeekReturn() {
        return ToString(Cpu.FunctionHandlerInUse.PeekReturnAddressOnMachineStackForCurrentFunction());
    }

    public string PeekReturn(CallType returnCallType) {
        return ToString(Cpu.FunctionHandlerInUse.PeekReturnAddressOnMachineStack(returnCallType));
    }

    public void Run() {
        State state = Cpu.State;
        FunctionHandler functionHandler = Cpu.FunctionHandler;
        try {
            if (!_dmaThreadStarted) {
                _dmaThread.Start();
                _dmaThreadStarted = true;
            }
            // Entry could be overridden and could throw exceptions
            functionHandler.Call(CallType.MACHINE, state.CS, state.IP, null, null, "entry", false);
            RunLoop();
        } catch (InvalidVMOperationException e) {
            e.Demystify();
            if (Debugger.IsAttached) {
                Debugger.Break();
            }

            throw;
        } catch (HaltRequestedException) {
            // Actually a signal generated code requested Exit
            Dispose(disposing: true);
        } catch (Exception e) {
            if (Debugger.IsAttached) {
                Debugger.Break();
            }

            e.Demystify();
            throw new InvalidVMOperationException(this, e);
        }
        MachineBreakpoints.OnMachineStop();
        functionHandler.Ret(CallType.MACHINE);
    }

    public bool IsPaused { get; private set; }

    private bool _exitEmulationLoop = false;

    public void ExitEmulationLoop() => _exitEmulationLoop = true;

    private void RunLoop() {
        _exitEmulationLoop = false;
        while (Cpu.IsRunning && !_exitEmulationLoop && !_disposed) {
            PauseIfAskedTo();
            if (RecordData) {
                MachineBreakpoints.CheckBreakPoint();
            }
            Cpu.ExecuteNextInstruction();
            Timer.Tick();
        }
    }

    public void PerformDmaTransfers() {
        if (!_disposed && !_exitDmaLoop) {
            _dmaResetEvent.Set();
        }
    }

    private void PauseIfAskedTo() {
        if(Gui?.PauseEmulatorOnStart == true) {
            Gui?.PauseEmulationOnStart();
            Gui?.WaitForContinue();
        }
        if (Gui?.IsPaused == true) {
            IsPaused = true;
            Paused?.Invoke();
            if (!_programExecutor.Step()) {
                Gui.IsPaused = true;
                Gui?.WaitForContinue();
            }
            Resumed?.Invoke();
            IsPaused = false;
        }
    }

    private static string ToString(SegmentedAddress? segmentedAddress) {
        if (segmentedAddress is not null) {
            return segmentedAddress.ToString();
        }

        return "null";
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _dmaResetEvent.Set();
                _exitDmaLoop = true;
                if (_dmaThread.IsAlive && _dmaThreadStarted) {
                    _dmaThread.Join();
                }
                _dmaResetEvent.Dispose();
                Midi.Dispose();
                SoundBlaster.Dispose();
                OPL3FM.Dispose();
                PcSpeaker.Dispose();
                MachineBreakpoints.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}