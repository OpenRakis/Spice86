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

/// <summary>Constructs native Tandy hardware without VGA platform branches in device operations.</summary>
public sealed class TandyMachineProfile : MachineProfile {
    /// <summary>Initializes the Tandy conventional-memory boundaries.</summary>
    public TandyMachineProfile() : base(MachineType.Tandy1000, 624, 640) {
    }

    /// <inheritdoc />
    public override ushort ReadEquipmentWord(IMemory memory) {
        return memory.UInt16[MemoryMap.BiosDataSegment, 0x10];
    }

    /// <inheritdoc />
    public override void ReserveFirmware(MemoryAsmWriter writer, bool enableSound) {
        TandyBios.ReserveFirmware(writer);
        base.ReserveFirmware(writer, enableSound);
    }

    /// <inheritdoc />
    public override void InstallFirmware(IMemory memory, BiosDataArea bios, InterruptVectorTable vectors,
        AssemblyRoutineInstaller installer, RealTimeClock clock) {
        _ = new TandyBios(memory, bios, vectors, TrueMemoryKb, installer, clock);
    }

    /// <inheritdoc />
    public override SystemBiosInt15Handler CreateSystemBios(Configuration configuration, IMemory memory,
        IFunctionHandlerProvider functions, Stack stack, State state, A20Gate a20Gate, BiosDataArea bios,
        DeviceScheduler scheduler, IOPortDispatcher ports, ILogger logger) {
        return new TandySystemBiosInt15Handler(configuration, memory, functions, stack, state, a20Gate, bios, scheduler, ports, logger);
    }

    /// <inheritdoc />
    public override VideoState CreateVideoState() => new(new TandyVideoState());

    /// <inheritdoc />
    public override VgaIoPortHandler CreateVideoPortHandler(State state, IOPortDispatcher ports, ILogger logger,
        VideoState videoState, IEmulatedClock clock, bool failOnUnhandledPort) {
        return new TandyIoPortHandler(state, ports, logger, videoState, NativeState(videoState), clock, failOnUnhandledPort);
    }

    /// <inheritdoc />
    public override Renderer CreateRenderer(IMemory memory, VideoState state, VgaBlinkState blink,
        ILogger logger, IVgaRenderer256Color renderer256Color) {
        return new TandyRenderer(memory, state, NativeState(state), blink, logger, renderer256Color);
    }

    /// <inheritdoc />
    public override VgaTimingEngine CreateVideoTimingEngine(VideoState state, DeviceScheduler scheduler,
        IEmulatedClock clock, Renderer renderer, VgaBlinkState blink) {
        return new TandyTimingEngine(state, NativeState(state), scheduler, clock, renderer, blink);
    }

    /// <inheritdoc />
    public override VgaFunctionality CreateVideoFunctions(IIndexable memory, InterruptVectorTable vectors,
        IIOPortHandler ports, BiosDataArea bios, VgaRom rom, VideoState state, bool bootUpInTextMode) {
        return new TandyVgaFunctionality(memory, vectors, ports, bios, rom, state, NativeState(state), bootUpInTextMode);
    }

    /// <inheritdoc />
    public override VgaBios CreateVideoBios(IMemory memory, IFunctionHandlerProvider functions, Stack stack,
        State state, VgaFunctionality videoFunctions, BiosDataArea bios, ILogger logger) {
        TandyVgaFunctionality nativeFunctions = videoFunctions as TandyVgaFunctionality
            ?? throw new ArgumentException("Tandy BIOS requires native video services.", nameof(videoFunctions));
        return new TandyVgaBios(memory, functions, stack, state, nativeFunctions, bios, logger);
    }

    private static TandyVideoState NativeState(VideoState state) {
        return state.Tandy ?? throw new ArgumentException("Tandy hardware requires native video registers.", nameof(state));
    }
}