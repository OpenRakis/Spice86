namespace Spice86.Emulator.Sound.Midi.MT32;

using System;

internal sealed class Mt32MidiDevice : MidiDevice {
    private readonly Lazy<Mt32Player> player;
    private bool disposed;

    public Mt32MidiDevice(string romsPath) {
        if (string.IsNullOrWhiteSpace(romsPath))
            throw new ArgumentNullException(nameof(romsPath));

        player = new Lazy<Mt32Player>(() => new(romsPath));
    }

    public override void Pause() {
        if (player.IsValueCreated)
            player.Value.Pause();
    }

    public override void Resume() {
        if (player.IsValueCreated)
            player.Value.Resume();
    }

    protected override void PlayShortMessage(uint message) => player.Value.PlayShortMessage(message);
    protected override void PlaySysex(ReadOnlySpan<byte> data) => player.Value.PlaySysex(data);

    protected override void Dispose(bool disposing) {
        if (!disposed) {
            if (disposing && player.IsValueCreated)
                player.Value.Dispose();

            disposed = true;
        }
    }
}
