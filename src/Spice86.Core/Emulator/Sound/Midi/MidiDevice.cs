namespace Spice86.Core.Emulator.Sound.Midi;
using System;

internal abstract class MidiDevice : IDisposable {
    private uint _currentMessage;
    private uint _bytesReceived;
    private uint _bytesExpected;
    private byte[] _currentSysex = new byte[128];
    private int _sysexIndex = -1;
    private static readonly uint[] _messageLength = { 3, 3, 3, 3, 2, 2, 3, 1 };

    protected MidiDevice() {
    }

    public void SendByte(byte value) {
        if (_sysexIndex == -1) {
            if (value == 0xF0 && _bytesExpected == 0) {
                _currentSysex[0] = 0xF0;
                _sysexIndex = 1;
                return;
            } else if ((value & 0x80) != 0) {
                _currentMessage = value;
                _bytesReceived = 1;
                _bytesExpected = _messageLength[(value & 0x70) >> 4];
            } else {
                if (_bytesReceived < _bytesExpected) {
                    _currentMessage |= (uint)(value << (int)(_bytesReceived * 8u));
                    _bytesReceived++;
                }
            }

            if (_bytesReceived >= _bytesExpected) {
                PlayShortMessage(_currentMessage);
                _bytesReceived = 0;
                _bytesExpected = 0;
            }
        } else {
            if (_sysexIndex >= _currentSysex.Length) {
                Array.Resize(ref _currentSysex, _currentSysex.Length * 2);
            }

            _currentSysex[_sysexIndex++] = value;

            if (value == 0xF7) {
                // do nothing for general midi
                PlaySysex(_currentSysex.AsSpan(0, _sysexIndex));
                _sysexIndex = -1;
            }
        }
    }
    public abstract void Pause();
    public abstract void Resume();
    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected abstract void PlayShortMessage(uint message);
    protected abstract void PlaySysex(ReadOnlySpan<byte> data);

    protected virtual void Dispose(bool disposing) {
    }
}
