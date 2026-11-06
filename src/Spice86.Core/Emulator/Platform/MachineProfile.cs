namespace Spice86.Core.Emulator.Platform;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;
using Spice86.Shared.Utils;

/// <summary>Platform-specific BIOS memory reporting.</summary>
/// <remarks>
/// Tandy boundaries follow DOSBox Staging, copyright the DOSBox Team and DOSBox Staging Team,
/// licensed under GPL-2.0-or-later.
/// </remarks>
public class MachineProfile {
    private static readonly MachineProfile PcProfile = new(MachineType.Pc, 640, 640);
    private static readonly MachineProfile TandyProfile = new TandyMachineProfile();

    /// <summary>Initializes a platform's conventional-memory boundaries.</summary>
    protected MachineProfile(MachineType machineType, ushort biosReportedMemoryKb, ushort trueMemoryKb) {
        MachineType = machineType;
        BiosReportedMemoryKb = biosReportedMemoryKb;
        TrueMemoryKb = trueMemoryKb;
    }

    /// <summary>Selected guest platform.</summary>
    public MachineType MachineType { get; }

    /// <summary>Whether native Tandy hardware is selected.</summary>
    public bool IsTandy => MachineType == MachineType.Tandy1000;

    /// <summary>Conventional memory reported by INT 12h.</summary>
    public ushort BiosReportedMemoryKb { get; }

    /// <summary>Conventional RAM backing before platform reservations.</summary>
    public ushort TrueMemoryKb { get; }

    /// <summary>Reads the platform's INT 11h equipment report.</summary>
    public virtual ushort ReadEquipmentWord(IMemory memory) => 0;

    /// <summary>Reserves firmware memory before provided handlers are written.</summary>
    public virtual void ReserveFirmware(MemoryAsmWriter writer, bool enableSound) {
        if (enableSound) {
            TandySoundBios.ReserveSoundBuffer(writer);
        }
    }

    /// <summary>Installs platform firmware after the shared interrupt handlers.</summary>
    public virtual void InstallFirmware(IMemory memory, BiosDataArea bios, InterruptVectorTable vectors,
        AssemblyRoutineInstaller installer, RealTimeClock clock) {
        memory.UInt16[0xF000, 0xFFF0] = 0xF4;
    }

    /// <summary>Creates the platform's system-services handler.</summary>
    public virtual SystemBiosInt15Handler CreateSystemBios(Configuration configuration, IMemory memory,
        IFunctionHandlerProvider functions, Stack stack, State state, A20Gate a20Gate, BiosDataArea bios,
        DeviceScheduler scheduler, IOPortDispatcher ports, ILogger logger) {
        return new SystemBiosInt15Handler(configuration, memory, functions, stack, state, a20Gate, bios, scheduler, ports, logger, false);
    }

    /// <summary>Creates registers and beam timing for the selected platform.</summary>
    public virtual VideoState CreateVideoState() => new();

    /// <summary>Registers the platform's video I/O ports.</summary>
    public virtual VgaIoPortHandler CreateVideoPortHandler(State state, IOPortDispatcher ports, ILogger logger,
        VideoState videoState, IEmulatedClock clock, bool failOnUnhandledPort) {
        return new VgaIoPortHandler(state, ports, logger, videoState, clock, failOnUnhandledPort);
    }

    /// <summary>Creates the renderer and its video-memory aperture.</summary>
    public virtual Renderer CreateRenderer(IMemory memory, VideoState state, VgaBlinkState blink,
        ILogger logger, IVgaRenderer256Color renderer256Color) {
        return new Renderer(memory, state, blink, logger, renderer256Color);
    }

    /// <summary>Creates the scanout scheduler for the platform.</summary>
    public virtual VgaTimingEngine CreateVideoTimingEngine(VideoState state, DeviceScheduler scheduler,
        IEmulatedClock clock, Renderer renderer, VgaBlinkState blink) {
        return new VgaTimingEngine(state, scheduler, clock, renderer, blink);
    }

    /// <summary>Creates the platform's BIOS video services.</summary>
    public virtual VgaFunctionality CreateVideoFunctions(IIndexable memory, InterruptVectorTable vectors,
        IIOPortHandler ports, BiosDataArea bios, VgaRom rom, VideoState state, bool bootUpInTextMode) {
        return new VgaFunctionality(memory, vectors, ports, bios, rom, state, bootUpInTextMode);
    }

    /// <summary>Creates the INT 10h handler for the platform's video services.</summary>
    public virtual VgaBios CreateVideoBios(IMemory memory, IFunctionHandlerProvider functions, Stack stack,
        State state, VgaFunctionality videoFunctions, BiosDataArea bios, ILogger logger) {
        return new VgaBios(memory, functions, stack, state, videoFunctions, bios, logger);
    }

    /// <summary>Resolves the immutable profile for a supported guest platform.</summary>
    public static MachineProfile For(MachineType machineType) => machineType switch {
        MachineType.Pc => PcProfile,
        MachineType.Tandy1000 => TandyProfile,
        _ => throw new ArgumentOutOfRangeException(nameof(machineType), machineType, "Unsupported guest platform.")
    };
}