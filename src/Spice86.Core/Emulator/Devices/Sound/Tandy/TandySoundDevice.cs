// SPDX-FileCopyrightText: 2019-2025 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2018 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Sound.Tandy;

using Microsoft.Extensions.Logging;

using Spice86.Core.CLI.RuntimeOptions;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Platform;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;

/// <summary>Owns native Tandy audio and its secondary-DMA conflict lifecycle.</summary>
public sealed class TandySoundDevice : IDisposable {
    private readonly State _state;
    private readonly IOPortDispatcher _ports;
    private readonly DmaBus _dmaBus;
    private readonly SoftwareMixer _mixer;
    private readonly IEmulatedClock _clock;
    private readonly TandyAudioOptions _options;
    private readonly ILogger _logger;
    private TandyPsg? _psg;
    private TandyDac? _dac;
    private bool _disposed;

    /// <summary>Raised when native DAC availability or the BIOS request changes.</summary>
    public event Action? AvailabilityChanged;
    /// <summary>Whether Tandy sound requests the BIOS DAC service, even in PSG-only mode.</summary>
    public bool BiosRequested { get; private set; }
    /// <summary>Whether the native DAC is available.</summary>
    public bool HasDac => _dac?.IsAvailable == true;

    /// <summary>Shuts down conflicting secondary DMA and installs the requested native DAC.</summary>
    public TandySoundDevice(State state, IOPortDispatcher ports, DmaBus dmaBus, DualPic pic,
        SoftwareMixer mixer, DeviceScheduler scheduler, IEmulatedClock clock,
        TandyAudioOptions options, ILogger logger) {
        _state = state;
        _ports = ports;
        _dmaBus = dmaBus;
        _mixer = mixer;
        _clock = clock;
        _options = options;
        _logger = logger;
        bool enabled = options.Mode != TandySoundMode.Off &&
            (options.Mode != TandySoundMode.Auto || options.MachineType == MachineType.Tandy1000);
        if (!enabled) {
            return;
        }
        dmaBus.ShutdownSecondaryController();
        BiosRequested = true;
        dmaBus.SecondaryControllerActivating += OnSecondaryControllerActivating;
        bool wantsDac = options.Mode == TandySoundMode.On || options.Mode == TandySoundMode.Auto;
        bool initialized = false;
        try {
            if (wantsDac) {
                _dac = new TandyDac(state, ports, dmaBus, pic, mixer, scheduler, clock,
                    options.DacFilter, OnDacEvicted, logger);
            }
            initialized = true;
        } finally {
            if (!initialized) {
                Dispose();
            }
        }
    }

    /// <summary>Installs the PSG after BIOS routing has been configured; safe without built-in BIOS.</summary>
    public void InitializePsg() {
        if (_disposed || !BiosRequested || _psg is not null) {
            return;
        }
        bool initialized = false;
        try {
            _psg = new TandyPsg(_state, _ports, _mixer, _clock,
                _options.Fadeout, _options.PsgFilter, _logger);
            initialized = true;
        } finally {
            if (!initialized) {
                Dispose();
            }
        }
    }

    private void OnDacEvicted() {
        _dac = null;
        AvailabilityChanged?.Invoke();
    }

    private void OnSecondaryControllerActivating() {
        Deactivate();
    }

    private void Deactivate() {
        BiosRequested = false;
        AvailabilityChanged?.Invoke();
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
        _dmaBus.SecondaryControllerActivating -= OnSecondaryControllerActivating;
        Deactivate();
    }
}