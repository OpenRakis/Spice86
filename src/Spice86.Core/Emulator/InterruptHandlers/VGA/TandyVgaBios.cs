// Native Tandy INT 10h dispatch adapted from DOSBox Staging.
// SPDX-FileCopyrightText: 2022-2025 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>Native Tandy INT 10h services without EGA, VGA, or VESA extensions.</summary>
public sealed class TandyVgaBios : VgaBios {
    private const byte VideoModeMask = 0x7F;
    private const byte DontClearMemoryFlag = 0x80;
    private readonly TandyVgaFunctionality _tandyFunctions;

    /// <summary>Creates native Tandy INT 10h services.</summary>
    public TandyVgaBios(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state, TandyVgaFunctionality vgaFunctions, BiosDataArea biosDataArea, ILogger loggerService)
        : base(memory, functionHandlerProvider, stack, state, vgaFunctions, biosDataArea, loggerService) {
        _tandyFunctions = vgaFunctions;
    }

    /// <inheritdoc />
    protected override void InitializeAdapterBiosArea() {
        // Native firmware does not initialize EGA/VGA capability data.
    }

    /// <inheritdoc />
    protected override byte ReportedVideoMode => _biosDataArea.VideoMode;

    /// <inheritdoc />
    public override void GetSetDisplayCombinationCode() {
        // Not supported by native Tandy hardware.
    }

    /// <inheritdoc />
    public override void VideoSubsystemConfiguration() {
        // Not supported by native Tandy hardware.
    }

    /// <inheritdoc />
    public override void LoadFontInfo() {
        // Not supported by native Tandy hardware.
    }

    /// <inheritdoc />
    public override void SetPaletteRegisters() {
        if (State.AL <= 2) {
            base.SetPaletteRegisters();
        }
    }

    /// <inheritdoc />
    public override void SelectActiveDisplayPage() {
        if ((State.AL & 0x80) != 0) {
            HandleTandyPageBank();
            return;
        }
        base.SelectActiveDisplayPage();
    }

    /// <inheritdoc />
    public override void SetVideoMode() {
        int modeId = State.AL & VideoModeMask;
        ModeFlags flags = ModeFlags.Legacy | (ModeFlags)_biosDataArea.ModesetCtl & (ModeFlags.NoPalette | ModeFlags.GraySum);
        if ((State.AL & DontClearMemoryFlag) != 0) {
            flags |= ModeFlags.NoClearMem;
        }
        _tandyFunctions.VgaSetMode(modeId, flags);
    }

    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        if (operation is 0x11 or 0x12 or 0x1A or 0x1B or 0x4F || (operation == 0x10 && State.AL > 2)) {
            return;
        }
        if (operation == 5 && (State.AL & 0x80) != 0) {
            HandleTandyPageBank();
            return;
        }
        if (!HasRunnable(operation)) {
            return;
        }
        base.Run();
    }

    /// <inheritdoc />
    public override void VesaFunctions() {
        // Not supported by native Tandy hardware.
    }

    /// <inheritdoc />
    public override void VbeGetControllerInfo() {
        // Not supported by native Tandy hardware.
    }

    /// <inheritdoc />
    public override void VbeGetModeInfo() {
        // Not supported by native Tandy hardware.
    }

    /// <inheritdoc />
    public override void VbeSetMode() {
        // Not supported by native Tandy hardware.
    }

    /// <inheritdoc />
    public override VideoFunctionalityInfo GetFunctionalityInfo() {
        State.AL = 0;
        return new VideoFunctionalityInfo(Memory, MemoryUtils.ToPhysicalAddress(State.ES, State.DI));
    }

    private void HandleTandyPageBank() {
        byte page = Memory.UInt8[0x40, 0x8A];
        switch (State.AL) {
            case 0x80:
                State.BH = (byte)(page & 7);
                State.BL = (byte)((page >> 3) & 7);
                break;
            case 0x81:
                page = (byte)((page & 0xC7) | ((State.BL & 7) << 3));
                break;
            case 0x82:
                page = (byte)((page & 0xF8) | (State.BH & 7));
                break;
            case 0x83:
                page = (byte)((page & 0xC0) | (State.BH & 7) | ((State.BL & 7) << 3));
                break;
        }
        _tandyFunctions.SetTandyPageBank(page);
    }
}