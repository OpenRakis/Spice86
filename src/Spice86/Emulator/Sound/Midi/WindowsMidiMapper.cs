namespace Spice86.Emulator.Sound.Midi;

using System;
using System.Runtime.Versioning;

/// <summary>
/// Provides access to the Windows MIDI mapper.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsMidiMapper : MidiDevice {
    private IntPtr midiOutHandle;

    public WindowsMidiMapper() {
        NativeMethods.midiOutOpen(out midiOutHandle, NativeMethods.MIDI_MAPPER, IntPtr.Zero, IntPtr.Zero, 0);
    }
    ~WindowsMidiMapper() => Dispose(false);

    protected override void PlayShortMessage(uint message) => NativeMethods.midiOutShortMsg(midiOutHandle, message);
    protected override void PlaySysex(ReadOnlySpan<byte> data) { }
    public override void Pause() {
        // ... don't pause ...
        //NativeMethods.midiOutReset(midiOutHandle);
    }

    public override void Resume() {
        // ... and restart, the music becomes forever silent !
        //NativeMethods.midiOutOpen(out midiOutHandle, NativeMethods.MIDI_MAPPER, IntPtr.Zero, IntPtr.Zero, 0);
    }

    protected override void Dispose(bool disposing) {
        if (midiOutHandle != IntPtr.Zero) {
            NativeMethods.midiOutClose(midiOutHandle);
            midiOutHandle = IntPtr.Zero;
        }
    }
}
