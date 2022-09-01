namespace Spice86.Core.Emulator.Sound.Midi.MT32;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Sound.Midi;

using System;

internal sealed class Mt32MidiDevice : MidiDevice {
    private readonly Lazy<Mt32Player> _player;
    private bool _disposed;

    public Mt32MidiDevice(string romsPath, Configuration configuration) {
        if (string.IsNullOrWhiteSpace(romsPath)) {
            throw new ArgumentNullException(nameof(romsPath));
        }

        _player = new Lazy<Mt32Player>(() => new(romsPath, configuration));
    }

    public override void Pause() {
        if (_player.IsValueCreated) {
            _player.Value.Pause();
        }
    }

    public override void Resume() {
        if (_player.IsValueCreated) {
            _player.Value.Resume();
        }
    }

    protected override void PlayShortMessage(uint message) => _player.Value.PlayShortMessage(message);
    protected override void PlaySysex(ReadOnlySpan<byte> data) => _player.Value.PlaySysex(data);

    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing && _player.IsValueCreated) {
                _player.Value.Dispose();
            }

            _disposed = true;
        }
    }
}
