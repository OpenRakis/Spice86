namespace Spice86.Core.Emulator.Sound.Midi;

using Spice86.Core.Emulator.Sound;

using System;
using System.Runtime.Versioning;

/// <summary>
/// Provides access to the Windows MIDI mapper.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsMidiMapper : MidiDevice {
    private IntPtr _midiOutHandle;

    public WindowsMidiMapper() {
        NativeMethods.midiOutOpen(out _midiOutHandle, NativeMethods.MIDI_MAPPER, IntPtr.Zero, IntPtr.Zero, 0);
    }
    ~WindowsMidiMapper() => Dispose(false);

    protected override void PlayShortMessage(uint message) => NativeMethods.midiOutShortMsg(_midiOutHandle, message);
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
        if (_midiOutHandle != IntPtr.Zero) {
            NativeMethods.midiOutClose(_midiOutHandle);
            _midiOutHandle = IntPtr.Zero;
        }
    }
}
