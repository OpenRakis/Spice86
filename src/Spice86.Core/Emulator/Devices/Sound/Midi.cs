namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Sound.Midi;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// MPU401 MIDI interface implementation.
/// </summary>
public sealed class Midi : DefaultIOPortHandler, IDisposable {
    private const int Command = 0x331;
    private const int Data = 0x330;

    private readonly GeneralMidi _generalMidi;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the MPU-401 MIDI interface.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="configuration">The emulator configuration.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public Midi(Machine machine, Configuration configuration, ILoggerService loggerService) : base(machine, configuration, loggerService) {
        _generalMidi = new GeneralMidi(configuration, loggerService);
        _machine.Paused += Machine_Paused;
        _machine.Resumed += Machine_Resumed;
    }

    private void Machine_Resumed() {
        _generalMidi.Resume();
    }

    private void Machine_Paused() {
        _generalMidi.Pause();
    }

    /// <inheritdoc />
    public override byte ReadByte(int port) {
        return _generalMidi.ReadByte(port);
    }

    /// <inheritdoc />
    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(Data, this);
        ioPortDispatcher.AddIOPortHandler(Command, this);
    }

    /// <inheritdoc />
    public override void WriteByte(int port, byte value) {
        _generalMidi.WriteByte(port, value);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _generalMidi.Dispose();
            }
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}