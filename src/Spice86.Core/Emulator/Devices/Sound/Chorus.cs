/*
    ==============================================================================
    This file is part of Tal-NoiseMaker by Patrick Kunz.

    Copyright(c) 2005-2010 Patrick Kunz, TAL
    Togu Audio Line, Inc.
    http://kunz.corrupt.ch

    This file may be licensed under the terms of of the
    GNU General Public License Version 2 (the "GPL").

    Software distributed under the License is distributed
    on an "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, either
    express or implied. See the GPL for the specific language
    governing rights and limitations.

    You should have received a copy of the GPL along with this
    program. If not, go to http://www.gnu.org/licenses/gpl.html
    or write to the Free Software Foundation, Inc.,
    51 Franklin Street, Fifth Floor, Boston, MA 02110-1301, USA.
    ==============================================================================
 */

namespace Spice86.Core.Emulator.Devices.Sound;

using System;

public sealed class Chorus {
    private readonly float _sampleRate;
    private readonly float _delayTime;

    private readonly Lfo _lfo;
    private readonly OnePoleLP _lp;

    private readonly int _delayLineLength;
    private readonly float[] _delayLineStart;
    private readonly int _delayLineEnd;
    private int _writePtr;
    private float _delayLineOutput;

    private readonly float _rate;

    // Runtime variables
    private float _offset;
    private float _frac;
    private int _ptr;
    private int _ptr2;

    private float _z1;

    // lfo
    private float _lfoPhase;
    private readonly float _lfoStepSize;
    private float _lfoSign;

    public Chorus(float sampleRate, float phase, float rate, float delayTime) {
        _sampleRate = sampleRate;
        _delayTime = delayTime;

        _lfo = new Lfo(sampleRate);
        _lp = new OnePoleLP();

        _delayLineLength = (int)MathF.Floor(delayTime * sampleRate * 0.001f) * 2;
        _delayLineStart = new float[_delayLineLength];
        _delayLineEnd = _delayLineStart.Length;
        _writePtr = 0;
        _delayLineOutput = 0.0f;
        _rate = rate;
        _z1 = 0.0f;
        _lfoPhase = phase * 2.0f - 1.0f;
        _lfoStepSize = 4.0f * rate / sampleRate;
        _lfoSign = 1.0f;

        _lfo.ResetPhase(phase);
        _lfo.SetRate(rate);

        _writePtr = 0;
    }

    public float Process(float sample) {
        // Get delay time
        _offset = (NextLFO() * 0.3f + 0.4f) * _delayTime * _sampleRate * 0.001f;

        // Compute the largest read pointer based on the offset
        _ptr = _writePtr - (int)MathF.Floor(_offset);
        if (_ptr < 0) {
            _ptr += _delayLineLength;
        }

        _ptr2 = _ptr - 1;
        if (_ptr2 < 0) {
            _ptr2 += _delayLineLength;
        }

        _frac = _offset - (int)MathF.Floor(_offset);
        _delayLineOutput = _delayLineStart[_ptr2] + _delayLineStart[_ptr] * (1.0f - _frac) - (1.0f - _frac) * _z1;
        _z1 = _delayLineOutput;

        // Low pass
        _lp.Tick(ref _delayLineOutput, 0.95f);

        // Write the input sample and any feedback to delayline
        _delayLineStart[_writePtr] = sample;

        // Increment buffer index and wrap if necesary
        if (++_writePtr >= _delayLineEnd) {
            _writePtr = 0;
        }

        return _delayLineOutput;
    }

    private float NextLFO() {
        if (_lfoPhase >= 1.0f) {
            _lfoSign = -1.0f;
        } else if (_lfoPhase <= -1.0f) {
            _lfoSign = +1.0f;
        }
        _lfoPhase += _lfoStepSize * _lfoSign;
        return _lfoPhase;
    }
}