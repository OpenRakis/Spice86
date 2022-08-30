using System.Text;

namespace Spice86.Core.Emulator.VM;

using Serilog;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.CPU;
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
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.SystemClock;
using Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.InterruptHandlers.Vga;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// Emulates an IBM PC
/// </summary>
public class Machine : IDisposable {
    private const int InterruptHandlersSegment = 0xF000;
    private readonly ProgramExecutor _programExecutor;
    private readonly List<DmaChannel> _dmaDeviceChannels = new();

    private bool _disposed;

    public DosMemoryManager DosMemoryManager => DosInt21Handler.DosMemoryManager;

    public bool RecordData { get; set; }

    public BiosEquipmentDeterminationInt11Handler BiosEquipmentDeterminationInt11Handler { get; }

    public BiosKeyboardInt9Handler BiosKeyboardInt9Handler { get; }

    public CallbackHandler CallbackHandler { get; }

    public Cpu Cpu { get; }

    public DosInt20Handler DosInt20Handler { get; }

    public DosInt21Handler DosInt21Handler { get; }

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
    /// </summary>
    public EnvironmentVariables EnvironmentVariables { get; } = new EnvironmentVariables();
    public OPL3FM OPL3FM { get; }

    public event Action? Paused;

    public event Action? Resumed;

    public Configuration Configuration { get; }

    public Machine(ProgramExecutor programExecutor, IGui? gui, IKeyScanCodeConverter? keyScanCodeConverter, CounterConfigurator counterConfigurator, ExecutionFlowRecorder executionFlowRecorder, Configuration configuration, bool recordData) {
        _programExecutor = programExecutor;
        Configuration = configuration;
        Gui = gui;
        RecordData = recordData;

        // A full 1MB of addressable memory :)
        Memory = new Memory(0x100000);
        Cpu = new Cpu(this, executionFlowRecorder, recordData);

        // Breakpoints
        MachineBreakpoints = new MachineBreakpoints(this);

        // IO devices
        IoPortDispatcher = new IOPortDispatcher(this, configuration);
        Cpu.IoPortDispatcher = IoPortDispatcher;

        DmaController = new DmaController(this, configuration);
        Register(DmaController);

        Pic = new Pic(this, true, configuration);
        Register(Pic);
        VgaCard = new VgaCard(this, gui, configuration);
        Register(VgaCard);
        Timer = new Timer(this, Pic, VgaCard, counterConfigurator, configuration);
        Register(Timer);
        Keyboard = new Keyboard(this, gui, keyScanCodeConverter, configuration);
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
        CallbackHandler = new CallbackHandler(this, InterruptHandlersSegment);
        Cpu.CallbackHandler = CallbackHandler;
        TimerInt8Handler = new TimerInt8Handler(this);
        Register(TimerInt8Handler);
        BiosKeyboardInt9Handler = new BiosKeyboardInt9Handler(this, keyScanCodeConverter);
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

    public void Run() {
        State state = Cpu.State;
        FunctionHandler functionHandler = Cpu.FunctionHandler;
        functionHandler.Call(CallType.MACHINE, state.CS, state.IP, null, null, "entry", false);
        try {
            RunLoop();
        } catch (InvalidVMOperationException) {
            throw;
        } catch (Exception e) {
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
            bool ranNextCpuInstruction = false;
            // https://techgenix.com/direct-memory-access/
            for (int i = 0; i < _dmaDeviceChannels.Count; i++) {
                DmaChannel dmaChannel = _dmaDeviceChannels[i];
                dmaChannel.Transfer(Memory);
                if (dmaChannel.TransferMode == DmaTransferMode.SingleCycle) {
                    RunNextCpuInstructionAndTimerTick();
                    ranNextCpuInstruction = true;
                }
            }
            if (!ranNextCpuInstruction) {
                RunNextCpuInstructionAndTimerTick();
            }
        }
    }

    private void RunNextCpuInstructionAndTimerTick() {
        PauseIfAskedTo();
        if (RecordData) {
            MachineBreakpoints.CheckBreakPoint();
        }
        Cpu.ExecuteNextInstruction();
        Timer.Tick();
    }

    private void PauseIfAskedTo() {
        if (Gui?.IsPaused == true) {
            IsPaused = true;
            Paused?.Invoke();
            if (!_programExecutor.Step()) {
                Gui.IsPaused = true;
                Gui?.WaitOne();
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
                SoundBlaster.Dispose();
                OPL3FM.Dispose();
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