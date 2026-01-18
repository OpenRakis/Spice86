// SPDX-FileCopyrightText: 2013-2025 Nuked-OPL3 by nukeykt
// SPDX-License-Identifier: LGPL-2.1

namespace Spice86.Libs.Sound.Devices.NukedOpl3;

using Spice86.Libs.Sound.Devices.AdlibGold;

[Flags]
public enum OplWriteResult {
    None = 0,
    TimerUpdated = 1 << 0,
    DataWrite = 1 << 1,
    AdLibGoldWrite = 1 << 2
}

/// <summary>
///     Mirrors DOSBox-staging's OPL/AdLib Gold register I/O handling.
/// </summary>
public sealed class OplIo {
    private readonly Opl3Chip _chip;
    private readonly byte[] _registerCache = new byte[512];
    private readonly Func<double> _timeProvider;
    private readonly OplTimers _timers;
    private AdLibGoldIo? _adLibGold;
    private bool _irqAsserted;
    private double _samplesPerTick;
    private ushort _selectedRegister;
    private double _tickOrigin;
    private bool _timingInitialized;

    public OplIo(Opl3Chip chip, Func<double>? timeProvider = null, AdLibGoldIo? adLibGold = null) {
        _chip = chip ?? throw new ArgumentNullException(nameof(chip));
        _timeProvider = timeProvider ?? (() => 0.0);
        _timers = new OplTimers(_timeProvider);
        _adLibGold = adLibGold;
    }

    public Action<bool>? OnIrqChanged { get; set; }

    internal void AttachAdLibGold(AdLibGoldIo? adLibGold) {
        _adLibGold = adLibGold;
    }

    public void Reset(uint sampleRate) {
        _chip.Reset(sampleRate);
        _timers.Reset();
        Array.Clear(_registerCache, 0, _registerCache.Length);
        _selectedRegister = 0;

        _samplesPerTick = sampleRate / 1000.0;
        _timingInitialized = _samplesPerTick > double.Epsilon;
        _tickOrigin = _timeProvider();
        UpdateIrqState(false);
    }

    public OplWriteResult WritePort(ushort port, byte value) {
        bool isDataPort = (port & 0x01) != 0;

        if (isDataPort) {
            if (_adLibGold is { Active: true } && port == IOplPort.AdLibGoldDataPortNumber) {
                _adLibGold.Write(value);
                return OplWriteResult.AdLibGoldWrite;
            }

            if (_timers.Write((byte)(_selectedRegister & 0xFF), value)) {
                UpdateTimerStatus();
                return OplWriteResult.TimerUpdated;
            }

            _chip.WriteRegisterBuffered(_selectedRegister, value);
            _registerCache[_selectedRegister & 0x1FF] = value;
            return OplWriteResult.DataWrite;
        }

        if (_adLibGold != null && port == IOplPort.AdLibGoldAddressPortNumber) {
            switch (value) {
                case 0xFF:
                    _adLibGold.Active = true;
                    return OplWriteResult.None;
                case 0xFE:
                    _adLibGold.Active = false;
                    return OplWriteResult.None;
            }

            if (_adLibGold.Active) {
                _adLibGold.Index = value;
                return OplWriteResult.None;
            }
        }

        _selectedRegister = ComputeRegisterAddress(port, value);
        return OplWriteResult.None;
    }

    public byte ReadPort(ushort port) {
        switch (_adLibGold) {
            case { Active: true } when port == IOplPort.AdLibGoldAddressPortNumber:
                return 0x00;
            case { Active: true } when port == IOplPort.AdLibGoldDataPortNumber:
                return _adLibGold.Read();
        }

        if ((port & 0x01) != 0) {
            return 0xFF;
        }

        return (port & 0x03) == 0x00 ? UpdateTimerStatus() : (byte)0xFF;
    }

    internal byte PeekCachedRegister(int index) {
        return _registerCache[index & 0x1FF];
    }

    public void FlushDueWritesUpTo(double currentTick) {
        if (_timingInitialized) {
            ulong inclusiveSample = ConvertTicksToSample(currentTick);
            _chip.ProcessWriteBufferUntil(inclusiveSample);
        }

        UpdateTimerStatus(currentTick);
    }

    public void AdvanceTimersOnly(double currentTick) {
        UpdateTimerStatus(currentTick);
    }

    public double? GetTicksUntilNextWrite(double currentTick) {
        if (!_timingInitialized) {
            return null;
        }

        ulong? nextSample = _chip.PeekNextBufferedWriteSample();
        if (!nextSample.HasValue) {
            return null;
        }

        double nextTick = ConvertSamplesToTicks(nextSample.Value);
        double delay = nextTick - currentTick;
        return delay < 0 ? 0.0 : delay;
    }

    public double? GetTicksUntilTimerOverflow(double currentTick) {
        return _timers.GetTicksUntilNextOverflow(currentTick);
    }

    private ushort ComputeRegisterAddress(ushort port, byte value) {
        ushort address = value;
        bool highBank = (port & 0x02) != 0;

        if (highBank && (address == 0x05 || _chip.NewM != 0)) {
            address = (ushort)(address | 0x100);
        }

        return (ushort)(address & 0x1FF);
    }

    private byte UpdateTimerStatus(double? explicitTime = null) {
        double time = explicitTime ?? _timeProvider();
        byte status = _timers.ReadStatus(time);
        UpdateIrqState((status & 0x80) != 0);
        return status;
    }

    private void UpdateIrqState(bool asserted) {
        if (_irqAsserted == asserted) {
            return;
        }

        _irqAsserted = asserted;
        OnIrqChanged?.Invoke(asserted);
    }

    private ulong ConvertTicksToSample(double ticks) {
        if (!_timingInitialized || ticks <= _tickOrigin) {
            return 0;
        }

        double delta = (ticks - _tickOrigin) * _samplesPerTick;
        if (delta <= 0) {
            return 0;
        }

        return (ulong)Math.Floor(delta);
    }

    private double ConvertSamplesToTicks(ulong samples) {
        if (!_timingInitialized) {
            return _tickOrigin;
        }

        double sampleCount = samples;
        return _tickOrigin + (sampleCount / _samplesPerTick);
    }

    private sealed class OplTimers {
        private readonly Func<double> _timeProvider;
        private readonly OplTimer _timer0 = new(80);
        private readonly OplTimer _timer1 = new(320);

        internal OplTimers(Func<double> timeProvider) {
            _timeProvider = timeProvider;
        }

        internal void Reset() {
            _timer0.Reset();
            _timer1.Reset();
        }

        internal bool Write(byte register, byte value) {
            switch (register) {
                case 0x02:
                    _timer0.Update(_timeProvider());
                    _timer0.SetCounter(value);
                    return true;
                case 0x03:
                    _timer1.Update(_timeProvider());
                    _timer1.SetCounter(value);
                    return true;
                case 0x04:
                    HandleControl(value);
                    return true;
                default:
                    return false;
            }
        }

        internal byte ReadStatus(double? absoluteTime = null) {
            double time = absoluteTime ?? _timeProvider();
            byte status = 0;

            if (_timer0.Update(time)) {
                status |= 0x40 | 0x80;
            }

            if (_timer1.Update(time)) {
                status |= 0x20 | 0x80;
            }

            return status;
        }

        internal double? GetTicksUntilNextOverflow(double currentTime) {
            double? timer0 = _timer0.GetTicksUntilOverflow(currentTime);
            double? timer1 = _timer1.GetTicksUntilOverflow(currentTime);

            if (timer0.HasValue && timer1.HasValue) {
                return Math.Min(timer0.Value, timer1.Value);
            }

            return timer0 ?? timer1;
        }

        private void HandleControl(byte value) {
            if ((value & 0x80) != 0) {
                _timer0.Reset();
                _timer1.Reset();
                return;
            }

            double time = _timeProvider();

            if ((value & 0x01) != 0) {
                _timer0.Start(time);
            } else {
                _timer0.Stop();
            }

            if ((value & 0x02) != 0) {
                _timer1.Start(time);
            } else {
                _timer1.Stop();
            }

            _timer0.SetMask((value & 0x40) != 0);
            _timer1.SetMask((value & 0x20) != 0);
        }
    }

    private sealed class OplTimer {
        private readonly double _clockInterval;
        private byte _counter;
        private double _counterInterval;
        private bool _enabled;
        private bool _masked;
        private bool _overflow;
        private double _start;
        private double _trigger;

        internal OplTimer(int micros) {
            _clockInterval = micros * 0.001;
            SetCounter(0);
        }

        internal bool Update(double time) {
            if (_enabled && time >= _trigger) {
                double deltaTime = time - _trigger;
                double counterMod = _counterInterval > 0.0 ? deltaTime % _counterInterval : 0.0;
                _start = time - counterMod;
                _trigger = _start + _counterInterval;
                if (!_masked) {
                    _overflow = true;
                }
            }

            return _overflow;
        }

        internal void Reset() {
            _overflow = false;
        }

        internal void SetCounter(byte value) {
            _counter = value;
            _counterInterval = (256 - _counter) * _clockInterval;
        }

        internal void SetMask(bool set) {
            _masked = set;
            if (_masked) {
                _overflow = false;
            }
        }

        internal void Start(double time) {
            if (_enabled) {
                return;
            }

            _enabled = true;
            _overflow = false;

            double clockMod = _clockInterval > 0.0 ? time % _clockInterval : 0.0;
            _start = time - clockMod;
            _trigger = _start + _counterInterval;
        }

        internal void Stop() {
            _enabled = false;
        }

        internal double? GetTicksUntilOverflow(double time) {
            if (_overflow || !_enabled || _masked) {
                return null;
            }

            double remaining = _trigger - time;
            return remaining <= 0 ? 0.0 : remaining;
        }
    }
}