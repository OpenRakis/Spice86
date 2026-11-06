// SPDX-FileCopyrightText: 2019-2025 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2018 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound.Tandy;

using Microsoft.Extensions.Logging;

using Spice86.Core.CLI.RuntimeOptions;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;

/// <summary>Owns native Tandy audio and its secondary-DMA conflict lifecycle.</summary>
public sealed class TandySoundDevice : ISecondaryDmaPortOwner, IDisposable {
    private readonly DmaBus _dmaBus;
    private TandyPsg? _psg;
    private TandyDac? _dac;
    private TandySoundBios? _biosSound;
    private bool _disposed;

    /// <summary>Whether Tandy sound requests the BIOS DAC service, even in PSG-only mode.</summary>
    public bool BiosRequested { get; private set; }
    /// <summary>Whether the native DAC is available.</summary>
    public bool HasDac => _dac?.IsAvailable == true;

    /// <summary>Installs native audio after the composition root has selected an enabled sound mode.</summary>
    public TandySoundDevice(State state, IOPortDispatcher ports, DmaBus dmaBus, DualPic pic,
        SoftwareMixer mixer, DeviceScheduler scheduler, IEmulatedClock clock,
        TandyAudioOptions options, ILogger logger) {
        _dmaBus = dmaBus;
        dmaBus.ClaimSecondaryControllerPorts(this);
        BiosRequested = true;
        bool wantsDac = options.Mode == TandySoundMode.On || options.Mode == TandySoundMode.Auto;
        bool initialized = false;
        try {
            if (wantsDac) {
                _dac = new TandyDac(state, ports, dmaBus, pic, mixer, scheduler, clock,
                    options.DacFilter, this, logger);
            }
            _psg = new TandyPsg(state, ports, mixer, clock,
                options.Fadeout, options.PsgFilter, logger);
            initialized = true;
        } finally {
            if (!initialized) {
                Dispose();
            }
        }
    }

    /// <summary>Handles an INT 1Ah sound function when an attached BIOS DAC route is available.</summary>
    /// <param name="operation">The function number from AH.</param>
    /// <returns>Whether the sound BIOS handled the function.</returns>
    public bool TryHandleBiosFunction(byte operation) => _biosSound?.TryHandle(operation, false) == true;

    internal void AttachBiosSound(TandySoundBios biosSound) {
        _biosSound = biosSound;
    }

    internal void DetachBiosSound(TandySoundBios biosSound) {
        if (ReferenceEquals(_biosSound, biosSound)) {
            _biosSound = null;
        }
    }

    internal void OnDacEvicted() {
        _dac = null;
        _biosSound?.RefreshRouting();
    }

    /// <inheritdoc />
    public void ReleaseSecondaryDmaPorts() {
        Deactivate();
    }

    private void Deactivate() {
        BiosRequested = false;
        _biosSound?.RefreshRouting();
        _dac?.Dispose();
        _dac = null;
        _psg?.Dispose();
        _psg = null;
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _dmaBus.ReleaseSecondaryControllerPorts(this);
        Deactivate();
    }
}