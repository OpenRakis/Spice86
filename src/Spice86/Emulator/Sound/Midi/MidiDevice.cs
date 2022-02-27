namespace Spice86.Emulator.Sound.Midi;     
using System;

internal abstract class MidiDevice : IDisposable {
    private uint currentMessage;
    private uint bytesReceived;
    private uint bytesExpected;
    private byte[] currentSysex = new byte[128];
    private int sysexIndex = -1;
    private static readonly uint[] messageLength = { 3, 3, 3, 3, 2, 2, 3, 1 };

    protected MidiDevice() {
    }

    public void SendByte(byte value) {
        if (sysexIndex == -1) {
            if (value == 0xF0 && bytesExpected == 0) {
                currentSysex[0] = 0xF0;
                sysexIndex = 1;
                return;
            } else if ((value & 0x80) != 0) {
                currentMessage = value;
                bytesReceived = 1;
                bytesExpected = messageLength[(value & 0x70) >> 4];
            } else {
                if (bytesReceived < bytesExpected) {
                    currentMessage |= (uint)(value << (int)(bytesReceived * 8u));
                    bytesReceived++;
                }
            }

            if (bytesReceived >= bytesExpected) {
                PlayShortMessage(currentMessage);
                bytesReceived = 0;
                bytesExpected = 0;
            }
        } else {
            if (sysexIndex >= currentSysex.Length)
                Array.Resize(ref currentSysex, currentSysex.Length * 2);

            currentSysex[sysexIndex++] = value;

            if (value == 0xF7) {
                // do nothing for general midi
                PlaySysex(currentSysex.AsSpan(0, sysexIndex));
                sysexIndex = -1;
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
