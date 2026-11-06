// SPDX-FileCopyrightText: 2022-2026 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.DeviceScheduler;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>Provides Tandy system configuration and unsupported-service behavior for INT 15h.</summary>
public sealed class TandySystemBiosInt15Handler : SystemBiosInt15Handler {
    private BiosSystemConfigurationTable? _systemConfigurationTable;
    private SegmentedAddress _systemConfigurationAddress;

    /// <summary>Creates native BIOS services without writing the PC reset vector.</summary>
    public TandySystemBiosInt15Handler(Configuration configuration, IMemory memory,
        IFunctionHandlerProvider functions, Stack stack, State state, A20Gate a20Gate, BiosDataArea bios,
        DeviceScheduler scheduler, IOPortDispatcher ports, ILogger logger)
        : base(configuration, memory, functions, stack, state, a20Gate, bios, scheduler, ports, logger, false) {
    }

    /// <inheritdoc />
    public override SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter writer) {
        SegmentedAddress handlerAddress = base.WriteAssemblyInRam(writer);
        while ((writer.CurrentAddress.Linear & 0x0F) != 0) {
            writer.WriteUInt8(0);
        }
        _systemConfigurationAddress = new SegmentedAddress((ushort)(writer.CurrentAddress.Linear >> 4), 0);
        for (int index = 0; index < BiosSystemConfigurationTable.AllocationSize; index++) {
            writer.WriteUInt8(0);
        }
        _systemConfigurationTable = new BiosSystemConfigurationTable(Memory, _systemConfigurationAddress.Linear);
        _systemConfigurationTable.InitializeTandy();
        return handlerAddress;
    }

    /// <inheritdoc />
    public override void GetSystemConfiguration(bool calledFromVm) {
        if (_systemConfigurationTable is null) {
            base.GetSystemConfiguration(calledFromVm);
            return;
        }
        _systemConfigurationTable.InitializeTandy();
        State.ES = _systemConfigurationAddress.Segment;
        State.BX = _systemConfigurationAddress.Offset;
        State.AH = 0;
        SetCarryFlag(false, calledFromVm);
    }

    /// <inheritdoc />
    public override void Run(int index) {
        if (!HasRunnable(index)) {
            Unsupported();
            return;
        }
        base.Run(index);
    }
}