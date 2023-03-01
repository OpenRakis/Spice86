using Spice86.Logging;
using Spice86.Shared.Interfaces;

namespace Spice86.Core.Emulator.Sound.Midi.MT32;

using Spice86.Core.Emulator.Sound.Midi;

using System;

internal sealed class Mt32MidiDevice : MidiDevice {
    private readonly Mt32Player _player;
    private bool _disposed;

    public Mt32MidiDevice(string romsPath, Configuration configuration, ILoggerService loggerService) {
        if (string.IsNullOrWhiteSpace(romsPath)) {
            throw new ArgumentNullException(nameof(romsPath));
        }

        _player = new Mt32Player(
            romsPath,
            loggerService,
            configuration);
    }

    public override void Pause() {
        _player.Pause();
    }

    public override void Resume() {
        _player.Resume();
    }

    protected override void PlayShortMessage(uint message) => _player.PlayShortMessage(message);
    protected override void PlaySysex(ReadOnlySpan<byte> data) => _player.PlaySysex(data);

    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _player.Dispose();
            }

            _disposed = true;
        }
    }
}
