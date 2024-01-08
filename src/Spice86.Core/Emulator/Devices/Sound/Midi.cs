namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Debugger;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Sound;
using Spice86.Core.Emulator.Sound.Midi;
using Spice86.Shared.Interfaces;

/// <summary>
/// MPU401 MIDI interface implementation.
/// </summary>
public sealed class Midi : DefaultIOPortHandler, IDisposable, IDebuggableComponent {
    /// <summary>
    /// The port number used for MIDI commands.
    /// </summary>
    public const int Command = 0x331;

    /// <summary>
    /// The port number used for MIDI data.
    /// </summary>
    public const int Data = 0x330;

    private readonly GeneralMidi _generalMidi;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the MPU-401 MIDI interface.
    /// </summary>
    /// <param name="audioPlayerFactory">The AudioPlayer factory.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="mt32RomsPath">Where are the MT-32 ROMs path located. Can be null if MT-32 isn't used.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public Midi(AudioPlayerFactory audioPlayerFactory, State state, string? mt32RomsPath, bool failOnUnhandledPort, ILoggerService loggerService) : base(state, failOnUnhandledPort, loggerService) {
        _generalMidi = new GeneralMidi(audioPlayerFactory, mt32RomsPath, loggerService);
    }

    
    /// <inheritdoc />
    public override byte ReadByte(int port) {
        UpdateLastPortRead(port);
        return _generalMidi.ReadByte(port);
    }

    /// <inheritdoc />
    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(Data, this);
        ioPortDispatcher.AddIOPortHandler(Command, this);
    }

    /// <inheritdoc />
    public override void WriteByte(int port, byte value) {
        UpdateLastPortWrite(port, value);
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

    public void Accept(IEmulatorDebugger emulatorDebugger) {
        emulatorDebugger.VisitExternalMidiDevice(this);
    }
}