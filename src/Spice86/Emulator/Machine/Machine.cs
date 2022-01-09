namespace Spice86.Emulator.Machine;

using Spice86.Emulator.Callback;
using Spice86.Emulator.Cpu;
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
using Spice86.Gui;

using System;

/// <summary>
/// Emulates an IBM PC
/// </summary>
public class Machine
{
    private static readonly int INTERRUPT_HANDLERS_SEGMENT = 0xF000;
    private Memory memory;
    private Cpu cpu;
    // IO Devices
    private IOPortDispatcher ioPortDispatcher;
    private Pic pic;
    private Timer timer;
    private SystemClockInt1AHandler systemClockInt1AHandler;
    private VgaCard vgaCard;
    private Keyboard keyboard;
    private Joystick joystick;
    private PcSpeaker pcSpeaker;
    private SoundBlaster soundBlaster;
    private GravisUltraSound gravisUltraSound;
    private Midi midi;
    private Gui gui;
    private CallbackHandler callbackHandler;
    // Services for int callbacks
    private TimerInt8Handler timerInt8Handler;
    private BiosKeyboardInt9Handler biosKeyboardInt9Handler;
    private VideoBiosInt10Handler videoBiosInt10Handler;
    private BiosEquipmentDeterminationInt11Handler biosEquipmentDeterminationInt11Handler;
    private SystemBiosInt15Handler systemBiosInt15Handler;
    private KeyboardInt16Handler keyboardInt16Handler;
    private DosInt20Handler dosInt20Handler;
    private DosInt21Handler dosInt21Handler;
    private MouseInt33Handler mouseInt33Handler;
    private MachineBreakpoints machineBreakpoints;
    private bool debugMode;
    public Machine(Gui gui, CounterConfigurator counterConfigurator, bool failOnUnhandledPort, bool debugMode)
    {
        this.gui = gui;
        this.debugMode = debugMode;
        InitHardware(counterConfigurator, failOnUnhandledPort);
        InitServices();
    }

    public virtual Memory GetMemory()
    {
        return memory;
    }

    public virtual Cpu GetCpu()
    {
        return cpu;
    }

    public virtual IOPortDispatcher GetIoPortDispatcher()
    {
        return ioPortDispatcher;
    }

    public virtual Pic GetPic()
    {
        return pic;
    }

    public virtual Timer GetTimer()
    {
        return timer;
    }

    public virtual SystemClockInt1AHandler GetSystemClockInt1AHandler()
    {
        return systemClockInt1AHandler;
    }

    public virtual VgaCard GetVgaCard()
    {
        return vgaCard;
    }

    public virtual Keyboard GetKeyboard()
    {
        return keyboard;
    }

    public virtual Joystick GetJoystick()
    {
        return joystick;
    }

    public virtual PcSpeaker GetPcSpeaker()
    {
        return pcSpeaker;
    }

    public virtual SoundBlaster GetSoundBlaster()
    {
        return soundBlaster;
    }

    public virtual GravisUltraSound GetGravisUltraSound()
    {
        return gravisUltraSound;
    }

    public virtual Midi GetMidi()
    {
        return midi;
    }

    public virtual Gui GetGui()
    {
        return gui;
    }

    public virtual CallbackHandler GetCallbackHandler()
    {
        return callbackHandler;
    }

    public virtual TimerInt8Handler GetTimerInt8Handler()
    {
        return timerInt8Handler;
    }

    public virtual BiosKeyboardInt9Handler GetBiosKeyboardInt9Handler()
    {
        return biosKeyboardInt9Handler;
    }

    public virtual VideoBiosInt10Handler GetVideoBiosInt10Handler()
    {
        return videoBiosInt10Handler;
    }

    public virtual BiosEquipmentDeterminationInt11Handler GetBiosEquipmentDeterminationInt11Handler()
    {
        return biosEquipmentDeterminationInt11Handler;
    }

    public virtual SystemBiosInt15Handler GetSystemBiosInt15Handler()
    {
        return systemBiosInt15Handler;
    }

    public virtual KeyboardInt16Handler GetKeyboardInt16Handler()
    {
        return keyboardInt16Handler;
    }

    public virtual DosInt20Handler GetDosInt20Handler()
    {
        return dosInt20Handler;
    }

    public virtual DosInt21Handler GetDosInt21Handler()
    {
        return dosInt21Handler;
    }

    public virtual MouseInt33Handler GetMouseInt33Handler()
    {
        return mouseInt33Handler;
    }

    public virtual MachineBreakpoints GetMachineBreakpoints()
    {
        return machineBreakpoints;
    }

    public virtual string PeekReturn()
    {
        return ToString(cpu.GetFunctionHandlerInUse().PeekReturnAddressOnMachineStackForCurrentFunction());
    }

    public virtual string PeekReturn(CallType returnCallType)
    {
        return ToString(cpu.GetFunctionHandlerInUse().PeekReturnAddressOnMachineStack(returnCallType));
    }

    private string ToString(SegmentedAddress segmentedAddress)
    {
        if (segmentedAddress != null)
        {
            return segmentedAddress.ToString();
        }

        return "null";
    }

    private void InitHardware(CounterConfigurator counterConfigurator, bool failOnUnhandledPort)
    {

        // A full 1MB of addressable memory :)
        memory = new Memory(0x100000);
        cpu = new Cpu(this, debugMode);

        // Breakpoints
        machineBreakpoints = new MachineBreakpoints(this);

        // IO devices
        ioPortDispatcher = new IOPortDispatcher(this, failOnUnhandledPort);
        cpu.SetIoPortDispatcher(ioPortDispatcher);
        pic = new Pic(this, true, failOnUnhandledPort);
        Register(pic);
        vgaCard = new VgaCard(this, gui, failOnUnhandledPort);
        Register(vgaCard);
        timer = new Timer(this, pic, vgaCard, counterConfigurator, failOnUnhandledPort);
        Register(timer);
        keyboard = new Keyboard(this, gui, failOnUnhandledPort);
        Register(keyboard);
        joystick = new Joystick(this, failOnUnhandledPort);
        Register(joystick);
        pcSpeaker = new PcSpeaker(this, failOnUnhandledPort);
        Register(pcSpeaker);
        soundBlaster = new SoundBlaster(this, failOnUnhandledPort);
        Register(soundBlaster);
        gravisUltraSound = new GravisUltraSound(this, failOnUnhandledPort);
        Register(gravisUltraSound);
        midi = new Midi(this, failOnUnhandledPort);
        Register(midi);
    }

    private void InitServices()
    {
        callbackHandler = new CallbackHandler(this, (ushort)INTERRUPT_HANDLERS_SEGMENT);
        cpu.SetCallbackHandler(callbackHandler);
        timerInt8Handler = new TimerInt8Handler(this);
        Register(timerInt8Handler);
        biosKeyboardInt9Handler = new BiosKeyboardInt9Handler(this);
        Register(biosKeyboardInt9Handler);
        videoBiosInt10Handler = new VideoBiosInt10Handler(this, vgaCard);
        videoBiosInt10Handler.InitRam();
        Register(videoBiosInt10Handler);
        biosEquipmentDeterminationInt11Handler = new BiosEquipmentDeterminationInt11Handler(this);
        Register(biosEquipmentDeterminationInt11Handler);
        systemBiosInt15Handler = new SystemBiosInt15Handler(this);
        Register(systemBiosInt15Handler);
        keyboardInt16Handler = new KeyboardInt16Handler(this, biosKeyboardInt9Handler.GetBiosKeyboardBuffer());
        Register(keyboardInt16Handler);
        systemClockInt1AHandler = new SystemClockInt1AHandler(this, timerInt8Handler);
        Register(systemClockInt1AHandler);
        dosInt20Handler = new DosInt20Handler(this);
        Register(dosInt20Handler);
        dosInt21Handler = new DosInt21Handler(this);
        Register(dosInt21Handler);
        mouseInt33Handler = new MouseInt33Handler(this, gui);
        Register(mouseInt33Handler);
    }

    public virtual void Register(IIOPortHandler ioPortHandler)
    {
        ioPortHandler.InitPortHandlers(ioPortDispatcher);
    }

    public virtual void Register(ICallback callback)
    {
        callbackHandler.AddCallback(callback);
    }

    public virtual void InstallAllCallbacksInInterruptTable()
    {
        callbackHandler.InstallAllCallbacksInInterruptTable();
    }

    public virtual void Run()
    {
        State state = cpu.GetState();
        FunctionHandler functionHandler = cpu.GetFunctionHandler();
        functionHandler.Call(CallType.MACHINE, state.GetCS(), state.GetIP(), null, null, () => "entry", false);
        try
        {
            RunLoop();
        }
        catch (InvalidVMOperationException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new InvalidVMOperationException(this, e);
        }

        machineBreakpoints.OnMachineStop();
        functionHandler.Ret(CallType.MACHINE);
    }

    private void RunLoop()
    {
        while (cpu.IsRunning)
        {
            if (debugMode)
            {
                machineBreakpoints.CheckBreakPoint();
            }

            cpu.ExecuteNextInstruction();
            timer.Tick();
        }
    }

    public virtual string DumpCallStack()
    {
        FunctionHandler inUse = cpu.GetFunctionHandlerInUse();
        string callStack = "";
        if (inUse.Equals(cpu.GetFunctionHandlerInExternalInterrupt()))
        {
            callStack += "From external interrupt:\\n";
        }

        callStack += inUse.DumpCallStack();
        return callStack;
    }
}