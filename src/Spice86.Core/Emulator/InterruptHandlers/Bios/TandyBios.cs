// SPDX-FileCopyrightText: 2022-2026 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Installs built-in Tandy platform firmware without replacing a guest-supplied BIOS.
/// </summary>
public sealed class TandyBios : IAssemblyRoutineWriter {
    private readonly BiosDataArea _biosDataArea;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly ushort _trueMemoryKb;
    private readonly AssemblyRoutineInstaller _assemblyRoutineInstaller;
    private readonly TandyBiosRom _rom;

    /// <summary>
    /// Installs native firmware after the shared interrupt handlers have been installed.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="biosDataArea">The shared BIOS data area.</param>
    /// <param name="interruptVectorTable">The shared interrupt vectors.</param>
    /// <param name="trueMemoryKb">Physical conventional-memory size.</param>
    /// <param name="assemblyRoutineInstaller">The existing firmware routine installer.</param>
    /// <param name="realTimeClock">The shared RTC and CMOS device.</param>
    public TandyBios(IMemory memory, BiosDataArea biosDataArea, InterruptVectorTable interruptVectorTable,
        ushort trueMemoryKb, AssemblyRoutineInstaller assemblyRoutineInstaller, RealTimeClock realTimeClock) {
        _biosDataArea = biosDataArea;
        _interruptVectorTable = interruptVectorTable;
        _trueMemoryKb = trueMemoryKb;
        _assemblyRoutineInstaller = assemblyRoutineInstaller;
        _rom = new TandyBiosRom(memory);
        Initialize();
        realTimeClock.SetEquipmentByte((byte)_biosDataArea.EquipmentListFlags);
    }

    /// <summary>
    /// Reserves the firmware tail, including the DAC silence buffer, before any routines are installed.
    /// </summary>
    /// <param name="memoryAsmWriter">The shared provided-handler writer.</param>
    public static void ReserveFirmware(MemoryAsmWriter memoryAsmWriter) {
        memoryAsmWriter.ReserveRange(0xFA000, 0x6000);
    }

    private void Initialize() {
        _biosDataArea.TrueConventionalMemorySizeKb = _trueMemoryKb;
        int parallelPorts = 0;
        for (int index = 0; index < 3; index++) {
            if (_biosDataArea.PortLpt[index] != 0) {
                parallelPorts++;
            }
        }
        ushort parallelFlags = parallelPorts == 2 ? (ushort)0x4000 : (ushort)0xC000;
        _biosDataArea.EquipmentListFlags = (ushort)((_biosDataArea.EquipmentListFlags & ~0xC130) | parallelFlags | 0x1026);
        for (int index = 0; index < 3; index++) {
            _biosDataArea.LptTimeout[index] = 1;
        }
        for (int index = 0; index < 4; index++) {
            _biosDataArea.ComTimeout[index] = 1;
        }
        SegmentedAddress resetHandler = _assemblyRoutineInstaller.InstallAssemblyRoutine(this, "tandy_bios_reset");
        _rom.Initialize(resetHandler);
        _interruptVectorTable[0x05] = TandyBiosRom.PrintScreenHandler;
        _interruptVectorTable[0x18] = resetHandler;
        _interruptVectorTable[0x19] = resetHandler;
        _interruptVectorTable[0x1D] = TandyBiosRom.VideoParameterTable;
    }

    /// <inheritdoc />
    public SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        SegmentedAddress address = memoryAsmWriter.CurrentAddress;
        // The built-in BIOS terminates on reset; it does not implement a ROM boot sequence.
        memoryAsmWriter.WriteUInt8(0xF4);
        return address;
    }
}