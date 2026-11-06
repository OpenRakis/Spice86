// SPDX-FileCopyrightText: 2020-2025 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Memory;

/// <summary>Maps the native Tandy aperture onto the RAM also accessed by CPU and DMA.</summary>
public sealed class TandyVideoMemory : IMemoryDevice {
    private readonly IMemoryDevice _ram;
    private readonly TandyVideoState _state;

    /// <summary>Creates a banked aperture over the machine's physical RAM.</summary>
    public TandyVideoMemory(IMemoryDevice ram, TandyVideoState state) {
        _ram = ram;
        _state = state;
    }

    /// <inheritdoc />
    public uint Size => 0x20000;

    private uint Translate(uint address) {
        if (!_state.ExtendedRam && address >= 0xB8000) {
            uint mask = (_state.MemoryBank & 1) != 0 ? 0x3FFFU : 0x7FFFU;
            return 0x80000U + (uint)_state.MemoryBank * 0x4000U + (address & mask);
        }
        return address;
    }

    /// <inheritdoc />
    public byte Read(uint address) => _ram.Read(Translate(address));
    /// <inheritdoc />
    public void Write(uint address, byte value) => _ram.Write(Translate(address), value);
    /// <inheritdoc />
    public IList<byte> GetSlice(int address, int length) {
        byte[] result = new byte[length];
        for (int i = 0; i < length; i++) {
            result[i] = Read((uint)(address + i));
        }
        return result;
    }

    /// <summary>Reads display data independently of the processor's selected bank.</summary>
    public byte ReadDisplay(int offset) {
        uint baseAddress = _state.ExtendedRam ? 0xA0000U : 0x80000U + (uint)_state.DrawBank * 0x4000U;
        uint mask = _state.ExtendedRam ? 0x1FFFFU : 0x7FFFU;
        return _ram.Read(baseAddress + ((uint)offset & mask));
    }
}