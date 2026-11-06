// SPDX-FileCopyrightText: 2019-2025 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2018 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Sound.Tandy;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

/// <summary>INT 1Ah Tandy sound services and IRQ-driven DMA buffer chaining.</summary>
public sealed class TandySoundBios : IAssemblyRoutineWriter, IDisposable {
    /// <summary>Stable callback identifier for the Tandy DAC interrupt glue.</summary>
    public const ushort IrqCallbackNumber = 0x10e;
    private const ushort BdaSegment = 0x40;
    private const ushort RemainingOffset = 0xd0;
    private const ushort DelayAmplitudeOffset = 0xd2;
    private const ushort PageStatusOffset = 0xd4;
    private const ushort SavedVectorOffset = 0xd6;
    private const ushort SilenceSegment = 0xf000;
    private const ushort SilenceOffset = 0xa084;
    private const ushort SilenceLength = 16;
    private readonly State _state;
    private readonly IMemory _memory;
    private readonly Stack _stack;
    private readonly IOPortDispatcher _ports;
    private readonly InterruptVectorTable _vectors;
    private readonly AssemblyRoutineInstaller _installer;
    private readonly TandySoundDevice _sound;
    private readonly SoundBlaster? _blaster;
    private SegmentedAddress? _irqAddress;
    private SegmentedAddress _completionAddress;
    private DacRoute? _route;
    private bool _initialized;
    private bool _disposed;

    /// <summary>Installs BIOS sound support after built-in interrupt and platform firmware routines.</summary>
    /// <remarks>Construction order preserves callback placement and the vectors saved for restoration.</remarks>
    public TandySoundBios(State state, IMemory memory, Stack stack, IOPortDispatcher ports,
        InterruptVectorTable vectors, AssemblyRoutineInstaller installer,
        TandySoundDevice sound, SoundBlaster? blaster) {
        _state = state;
        _memory = memory;
        _stack = stack;
        _ports = ports;
        _vectors = vectors;
        _installer = installer;
        _sound = sound;
        _blaster = blaster;
        Initialize();
    }

    /// <summary>Protects the fixed BIOS DAC buffer before interrupt routines are installed on either platform.</summary>
    /// <param name="writer">The shared firmware routine writer.</param>
    public static void ReserveSoundBuffer(MemoryWriter writer) {
        writer.ReserveRange(((uint)SilenceSegment << 4) + SilenceOffset, SilenceLength);
    }

    private void Initialize() {
        if (_initialized || _disposed || !_sound.BiosRequested) {
            return;
        }
        _initialized = true;
        _sound.AttachBiosSound(this);
        _blaster?.AttachBiosSound(this);
        RefreshRouting();
    }

    private ushort Remaining {
        get => _memory.UInt16[BdaSegment, RemainingOffset];
        set => _memory.UInt16[BdaSegment, RemainingOffset] = value;
    }

    private ushort DelayAmplitude {
        get => _memory.UInt16[BdaSegment, DelayAmplitudeOffset];
        set => _memory.UInt16[BdaSegment, DelayAmplitudeOffset] = value;
    }

    private byte PageStatus {
        get => _memory.UInt8[BdaSegment, PageStatusOffset];
        set => _memory.UInt8[BdaSegment, PageStatusOffset] = value;
    }

    private SegmentedAddress SavedVector {
        get => _memory.SegmentedAddress16[BdaSegment, SavedVectorOffset];
        set => _memory.SegmentedAddress16[BdaSegment, SavedVectorOffset] = value;
    }

    internal void RefreshRouting() {
        if (!_initialized || _disposed) {
            return;
        }
        ShutdownRoute();
        if (!_sound.BiosRequested) {
            return;
        }
        if (_blaster?.IsBiosDacAvailable == true) {
            _route = new DacRoute(_blaster.BaseAddress, _blaster.IRQ, _blaster.LowDma, true);
            _blaster.UseBiosSoundWarmup();
        } else if (_sound.HasDac) {
            _route = new DacRoute(TandyDac.BasePort, TandyDac.Irq, TandyDac.Dma, false);
        }
        if (_route is not DacRoute route) {
            return;
        }
        if (_irqAddress is null) {
            _irqAddress = _installer.InstallAssemblyRoutine(this, "tandy_dac_irq");
        }
        Remaining = 0;
        DelayAmplitude = 0;
        PageStatus = 0xff;
        SavedVector = _vectors[route.Vector];
        for (ushort index = 0; index < SilenceLength; index++) {
            _memory.UInt8[SilenceSegment, (ushort)(SilenceOffset + index)] = 0x80;
        }
    }

    /// <summary>Returns true if an available Tandy BIOS route handled the requested function.</summary>
    public bool TryHandle(byte function, bool calledFromVm) {
        if (_route is not DacRoute route || _disposed || function is < 0x81 or > 0x85) {
            return false;
        }
        switch (function) {
            case 0x81:
                _state.AX = route.IsSoundBlaster ? TandyDac.BasePort : route.Port;
                SetCarryFlag(TransferInProgress(route), calledFromVm);
                break;
            case 0x82:
            case 0x83:
                if (TransferInProgress(route)) {
                    _state.AH = 0;
                    SetCarryFlag(true, calledFromVm);
                    break;
                }
                Remaining = _state.CX;
                DelayAmplitude = (ushort)((_state.DX & 0xfff) | ((_state.AL & 7) << 13));
                uint bufferAddress = ((uint)_state.ES << 4) + _state.BX;
                SetupTransfer(route, bufferAddress, function == 0x83);
                _state.AH = 0;
                SetCarryFlag(false, calledFromVm);
                break;
            case 0x84:
                _state.AH = 0;
                Remaining = 10;
                DelayAmplitude = 0x1c;
                SetupTransfer(route, ((uint)SilenceSegment << 4) + SilenceOffset, true);
                SetCarryFlag(false, calledFromVm);
                break;
            case 0x85:
                if (!route.IsSoundBlaster) {
                    Write(route.Port, Read(route.Port) & 0xe0);
                }
                _state.AH = 0;
                SetCarryFlag(false, calledFromVm);
                break;
        }
        return true;
    }

    private void SetCarryFlag(bool value, bool calledFromVm) {
        _state.CarryFlag = value;
        if (calledFromVm) {
            _stack.SetFlagOnInterruptStack(Flags.Carry, value);
        }
    }

    private bool TransferInProgress(DacRoute route) {
        if (Remaining != 0) {
            return true;
        }
        if (PageStatus == 0xff) {
            return false;
        }
        Write(0x0c, 0);
        ushort countPort = (ushort)(route.Dma * 2 + 1);
        int count = Read(countPort);
        count |= Read(countPort) << 8;
        if (count == 0xffff) {
            return false;
        }
        return !(count < 0x10 && PageStatus == 0x0f && DelayAmplitude == 0x1c);
    }

    private void SetupTransfer(DacRoute route, uint bufferAddress, bool playback) {
        ushort length = Remaining;
        if (length == 0 || _irqAddress is not SegmentedAddress irqAddress) {
            return;
        }
        SegmentedAddress current = _vectors[route.Vector];
        if (current != irqAddress) {
            SavedVector = current;
            _vectors[route.Vector] = irqAddress;
        }
        if (route.IsSoundBlaster) {
            Write((ushort)(route.Port + 0xc), 0xd0);
            Write(0x21, Read(0x21) & ~(1 << route.Irq));
            Write((ushort)(route.Port + 0xc), 0xd1);
        } else {
            Write(route.Port, Read(route.Port) & 0x60);
            Write(0x21, Read(0x21) & ~(1 << route.Irq));
        }
        Write(0x0a, 4 | route.Dma);
        Write(0x0c, 0);
        Write(0x0b, (playback ? 0x48 : 0x44) | route.Dma);
        byte page = (byte)((bufferAddress >> 16) & 0xff);
        ushort addressPort = (ushort)(route.Dma * 2);
        Write(addressPort, (int)bufferAddress & 0xff);
        Write(addressPort, (int)(bufferAddress >> 8) & 0xff);
        ushort pagePort = route.Dma switch {
            0 => 0x87,
            1 => 0x83,
            2 => 0x81,
            3 => 0x82,
            _ => throw new InvalidOperationException("BIOS Tandy sound requires a primary 8-bit DMA channel.")
        };
        Write(pagePort, page);
        PageStatus = page;
        int transferLength = Math.Min(length, 0x10000 - (int)(bufferAddress & 0xffff));
        Remaining = (ushort)(length - transferLength);
        int count = transferLength - 1;
        Write((ushort)(addressPort + 1), count & 0xff);
        Write((ushort)(addressPort + 1), (count >> 8) & 0xff);
        int delay = DelayAmplitude & 0xfff;
        int amplitude = (DelayAmplitude >> 13) & 7;
        if (route.IsSoundBlaster) {
            Write(0x0a, route.Dma);
            ushort commandPort = (ushort)(route.Port + 0xc);
            Write(commandPort, 0x40);
            Write(commandPort, 256 - delay * 100 / 358);
            Write(commandPort, playback ? 0x14 : 0x24);
            Write(commandPort, count & 0xff);
            Write(commandPort, (count >> 8) & 0xff);
        } else {
            Write(route.Port, (Read(route.Port) & 0x7c) | (playback ? 3 : 2));
            Write((ushort)(route.Port + 2), delay & 0xff);
            Write((ushort)(route.Port + 3), ((delay >> 8) & 15) | (amplitude << 5));
            Write(route.Port, (Read(route.Port) & 0x7c) | (playback ? 0x1f : 0x1e));
            Write(0x0a, route.Dma);
        }
        if (!playback) {
            DelayAmplitude = (ushort)(delay | 0x1000);
        }
    }

    private void HandleIrq() {
        if (_route is not DacRoute route || _disposed) {
            return;
        }
        if (!route.IsSoundBlaster) {
            Read(route.Port);
        }
        if (Remaining != 0) {
            Write(0x20, 0x20);
            if (route.IsSoundBlaster) {
                Read((ushort)(route.Port + 0xe));
            }
            byte nextPage = unchecked((byte)(PageStatus + 1));
            PageStatus = nextPage;
            bool recording = (DelayAmplitude & 0x1000) != 0;
            if (recording) {
                DelayAmplitude = (ushort)(DelayAmplitude & ~0x1000);
            }
            SetupTransfer(route, (uint)nextPage << 16, !recording);
            return;
        }
        _vectors[route.Vector] = SavedVector;
        if (route.IsSoundBlaster) {
            Write((ushort)(route.Port + 0xc), 0xd3);
            Read((ushort)(route.Port + 0xe));
        }
        _state.CS = _completionAddress.Segment;
        _state.IP = _completionAddress.Offset;
    }

    /// <inheritdoc />
    public SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter writer) {
        SegmentedAddress start = writer.CurrentAddress;
        writer.RegisterAndWriteCallback(IrqCallbackNumber, HandleIrq);
        writer.WriteIret();
        _completionAddress = writer.CurrentAddress;
        writer.WriteBytes([0x50, 0xb8, 0xfb, 0x91, 0xcd, 0x15, 0xfa, 0xb0, 0x20, 0xe6, 0x20, 0x58, 0xcf]);
        return start;
    }

    private byte Read(ushort port) => _ports.ReadByte(port);
    private void Write(ushort port, int value) => _ports.WriteByte(port, unchecked((byte)value));

    private void ShutdownRoute() {
        if (_route is DacRoute route) {
            if (route.IsSoundBlaster && _blaster is not null) {
                _blaster.StopBiosDacTransfer();
            }
            if (_irqAddress is SegmentedAddress irqAddress && _vectors[route.Vector] == irqAddress) {
                _vectors[route.Vector] = SavedVector;
            }
        }
        PageStatus = 0;
        _route = null;
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        if (!_initialized) {
            return;
        }
        _sound.DetachBiosSound(this);
        _blaster?.DetachBiosSound(this);
        ShutdownRoute();
    }

    private readonly record struct DacRoute(ushort Port, byte Irq, byte Dma, bool IsSoundBlaster) {
        internal byte Vector => (byte)(Irq < 8 ? Irq + 8 : Irq + 0x70 - 8);
    }
}