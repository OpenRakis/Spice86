namespace Spice86.Core.Emulator.Sound.Midi;

using Spice86.Core.Emulator.Pause;

using System;

/// <summary>
/// The base class for all classes talking to an external MIDI device.
/// </summary>
internal abstract class MidiDevice : Pauseable, IDisposable {
    private uint _currentMessage;
    private uint _bytesReceived;
    private uint _bytesExpected;
    private byte[] _currentSysex = new byte[128];
    private int _sysexIndex = -1;
    private static readonly uint[] MessageLength = { 3, 3, 3, 3, 2, 2, 3, 1 };

    /// <summary>
    /// Sends a byte to the MIDI device.
    /// </summary>
    /// <param name="value">The value to send.</param>
    public void SendByte(byte value) {
        if (_sysexIndex == -1) {
            if (value == 0xF0 && _bytesExpected == 0) {
                _currentSysex[0] = 0xF0;
                _sysexIndex = 1;
                return;
            } else if ((value & 0x80) != 0) {
                _currentMessage = value;
                _bytesReceived = 1;
                _bytesExpected = MessageLength[(value & 0x70) >> 4];
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
                // do nothing for General MIDI
                PlaySysex(_currentSysex.AsSpan(0, _sysexIndex));
                _sysexIndex = -1;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Plays a short MIDI message.
    /// </summary>
    /// <param name="message">The message to play.</param>
    protected abstract void PlayShortMessage(uint message);

    
    /// <summary>
    /// Plays a SysEx MIDI message.
    /// </summary>
    /// <param name="data">The data to play.</param>
    protected abstract void PlaySysex(ReadOnlySpan<byte> data);

    /// <summary>
    /// Releases the unmanaged resources used by the MIDI device and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing) {
    }
}
