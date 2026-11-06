// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) Nicola Salmoria, with contributions by others.
//
// Redistribution and use in source and binary forms, with or without modification,
// are permitted provided that the following conditions are met:
// 1. Redistributions of source code must retain the above copyright notice, this
//    list of conditions and the following disclaimer.
// 2. Redistributions in binary form must reproduce the above copyright notice,
//    this list of conditions and the following disclaimer in the documentation
//    and/or other materials provided with the distribution.
// 3. Neither the name of the copyright holder nor the names of its contributors
//    may be used to endorse or promote products derived from this software without
//    specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

namespace Spice86.Core.Emulator.Devices.Sound.Tandy;

/// <summary>
/// NCR8496 selected behavior from DOSBox Staging's MAME SN76496 core.
/// </summary>
public sealed class Ncr8496 {
    /// <summary>The native Tandy oscillator frequency.</summary>
    public const int SourceClockHz = 14318180 / 4;
    /// <summary>Rounded synthesis rate, matching the upstream render divisor.</summary>
    public const int RenderRateHz = (SourceClockHz + 15) / 16;
    private const int RoundedClockHz = RenderRateHz * 16;
    private const int RateMax = 1 << 10;
    private const int FeedbackMask = 0x8000;
    private readonly int[] _registers = new int[8];
    private readonly int[] _volumes = new int[4];
    private readonly int[] _periods = new int[4];
    private readonly int[] _counts = new int[4];
    private readonly int[] _outputs = new int[4];
    private readonly int[] _volumeTable = new int[16];
    private readonly int _rateAdd = (int)(RateMax * (RenderRateHz / (double)(RoundedClockHz / 2)));
    private int _lastRegister = 3;
    private int _rng = FeedbackMask;
    private int _currentClock = 7;
    private int _rateCounter;

    /// <summary>Initializes the selected chip's reset state and attenuation table.</summary>
    public Ncr8496() {
        const int MaxOutputPerChannel = short.MaxValue / 4;
        double output = MaxOutputPerChannel;
        for (int gain = 0; gain < 16; gain++) {
            output *= 1.023292992;
        }
        for (int index = 0; index < 15; index++) {
            _volumeTable[index] = Math.Min((int)output, MaxOutputPerChannel);
            output /= 1.258925412;
        }
    }

    /// <summary>Applies a latch or data write, including NCR-specific mirrored-write rules.</summary>
    public void Write(byte data) {
        int register;
        if ((data & 0x80) != 0) {
            register = (data & 0x70) >> 4;
            _lastRegister = register;
            if (register == 6 && (data & 4) != (_registers[6] & 4)) {
                _rng = FeedbackMask;
            }
            _registers[register] = (_registers[register] & 0x3f0) | (data & 15);
        } else {
            register = _lastRegister;
            if ((register & 1) != 0 || register == 6) {
                return;
            }
        }

        int channel = register >> 1;
        switch (register) {
            case 0:
            case 2:
            case 4:
                if ((data & 0x80) == 0) {
                    _registers[register] = (_registers[register] & 15) | ((data & 0x3f) << 4);
                }
                _periods[channel] = _registers[register] == 0 ? 0x400 : _registers[register];
                if (register == 4 && (_registers[6] & 3) == 3) {
                    _periods[3] = _periods[2] << 1;
                }
                break;
            case 1:
            case 3:
            case 5:
            case 7:
                _volumes[channel] = _volumeTable[data & 15];
                break;
            case 6:
                int noise = _registers[6] & 3;
                _periods[3] = noise == 3 ? _periods[2] << 1 : 1 << (5 + noise);
                break;
        }
    }

    /// <summary>Renders one signed mono sample at <see cref="RenderRateHz"/>.</summary>
    public short RenderSample() {
        do {
            if (_currentClock > 0) {
                _currentClock--;
            } else {
                _currentClock = 7;
                ClockChannels();
            }
            _rateCounter += _rateAdd;
        } while (_rateCounter < RateMax);
        _rateCounter -= RateMax;
        int output = 0;
        for (int channel = 0; channel < 4; channel++) {
            if (_outputs[channel] != 0) {
                output += _volumes[channel];
            }
        }
        return (short)-output;
    }

    private void ClockChannels() {
        for (int channel = 0; channel < 3; channel++) {
            if (--_counts[channel] <= 0) {
                _outputs[channel] ^= 1;
                _counts[channel] = _periods[channel];
            }
        }
        if (--_counts[3] > 0) {
            return;
        }
        bool feedback = ((_rng & 0x02) != 0) != (((_rng & 0x20) != 0x20) && (_registers[6] & 4) != 0);
        _rng >>= 1;
        if (feedback) {
            _rng |= FeedbackMask;
        }
        _outputs[3] = _rng & 1;
        _counts[3] = _periods[3];
    }
}