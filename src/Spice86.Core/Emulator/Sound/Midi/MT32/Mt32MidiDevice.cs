namespace Spice86.Core.Emulator.Sound.Midi.MT32;

using Spice86.Shared.Interfaces;
using Spice86.Core.Emulator.Sound.Midi;

/// <summary>
/// A MIDI device implementation for playing MIDI files on an MT-32 sound module.
/// </summary>
internal sealed class Mt32MidiDevice : MidiDevice {
    /// <summary>
    /// The MT-32 player instance associated with this device.
    /// </summary>
    private readonly Mt32Player _player;
    
    /// <summary>
    /// Indicates whether this object has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Constructs an instance of <see cref="Mt32MidiDevice"/>.
    /// </summary>
    /// <param name="audioPlayerFactory">The AudioPlayer factory.</param>
    /// <param name="romsPath">The path to the MT-32 ROM files.</param>
    /// <param name="loggerService">The logger service to use for logging messages.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="romsPath"/> is <c>null</c> or empty.</exception>
    public Mt32MidiDevice(AudioPlayerFactory audioPlayerFactory, string romsPath, ILoggerService loggerService) {
        if (string.IsNullOrWhiteSpace(romsPath)) {
            throw new ArgumentNullException(nameof(romsPath));
        }
        _player = new Mt32Player(audioPlayerFactory, romsPath, loggerService);
    }
    
    /// <inheritdoc/>
    protected override void PlayShortMessage(uint message) => _player.PlayShortMessage(message);
    
    /// <inheritdoc/>
    protected override void PlaySysex(ReadOnlySpan<byte> data) => _player.PlaySysex(data);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _player.Dispose();
            }

            _disposed = true;
        }
    }
}
