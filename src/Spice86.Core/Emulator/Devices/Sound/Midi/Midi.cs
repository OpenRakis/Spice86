namespace Spice86.Core.Emulator.Devices.Sound.Midi;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound.Midi.MT32;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// MPU401 MIDI interface implementation.
/// </summary>
public sealed class Midi : DefaultIOPortHandler, IDisposable {
    private readonly MidiDevice _midiMapper;
    private readonly Queue<byte> _dataBytes = new();

    /// <summary>
    /// The port number used to send and receive MIDI data.
    /// </summary>
    public const int DataPort = 0x330;

    /// <summary>
    /// The port number used to send and receive MIDI status information.
    /// </summary>
    public const int StatusPort = 0x331;

    /// <summary>
    /// The MIDI command used to reset all MIDI devices in a system.
    /// </summary>
    public const byte ResetCommand = 0xFF;

    /// <summary>
    /// The MIDI command used to enter UART (Universal Asynchronous Receiver/Transmitter) mode.
    /// </summary>
    public const byte EnterUartModeCommand = 0x3F;

    /// <summary>
    /// The MIDI command used by a receiving device to acknowledge receipt of a command.
    /// </summary>
    public const byte CommandAcknowledge = 0xFE;
    
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the MPU-401 MIDI interface.
    /// </summary>
    /// <param name="softwareMixer">The emulator's sound mixer.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="pauseHandler">The class that reacts to and notifies about emulation pause/resume events</param>
    /// <param name="mt32RomsPath">Where are the MT-32 ROMs path located. Can be null if MT-32 isn't used.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public Midi(SoftwareMixer softwareMixer, State state, IPauseHandler pauseHandler, string? mt32RomsPath, bool failOnUnhandledPort, ILoggerService loggerService) : base(state, failOnUnhandledPort, loggerService) {
        Mt32RomsPath = mt32RomsPath;
        // the external MIDI device (external General MIDI or external Roland MT-32).
        if (!string.IsNullOrWhiteSpace(Mt32RomsPath) && File.Exists(Mt32RomsPath)) {
            _midiMapper = new Mt32MidiDevice(new SoundChannel(softwareMixer, "MT-32"), Mt32RomsPath, loggerService);
        } else {
            _midiMapper = new GeneralMidiDevice(
                new SoundChannel(softwareMixer, "General MIDI"),
                loggerService,
                pauseHandler);
        }
    }

    /// <summary>
    /// Gets the current state of the General MIDI device.
    /// </summary>
    public GeneralMidiState State { get; private set; }

    /// <summary>
    /// Gets or sets the path where MT-32 roms are stored.
    /// </summary>
    public string? Mt32RomsPath { get; }

    /// <summary>
    /// Gets whether we are emulating an MT-32 device.
    /// </summary>
    public bool UseMT32 => !string.IsNullOrWhiteSpace(Mt32RomsPath);

    /// <summary>
    /// Gets the current value of the MIDI status port.
    /// </summary>
    public GeneralMidiStatus Status {
        get {
            GeneralMidiStatus status = GeneralMidiStatus.OutputReady;

            if (_dataBytes.Count > 0) {
                status |= GeneralMidiStatus.InputReady;
            }

            return status;
        }
    }

    /// <summary>
    /// All the input ports usable with the device.
    /// </summary>
    public IEnumerable<int> InputPorts => new int[] { DataPort, StatusPort };
    
    /// <summary>
    /// Read a byte from a port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <returns>The value from the port.</returns>
    /// <exception cref="ArgumentException">When the port isn't recognized.</exception>
    public override ushort ReadWord(int port) => ReadByte(port);

    /// <summary>
    /// All the output ports usable with the device.
    /// </summary>
    public IEnumerable<int> OutputPorts => new int[] { 0x330, 0x331 };

    /// <summary>
    /// Write a word to the device.
    /// </summary>
    /// <param name="port">The port to write to.</param>
    /// <param name="value">The value being written.</param>
    public override void WriteWord(int port, ushort value) => WriteByte(port, (byte)value);
    
    /// <summary>
    /// The port number used for MIDI commands.
    /// </summary>
    public const int Command = 0x331;

    /// <summary>
    /// The port number used for MIDI data.
    /// </summary>
    public const int Data = 0x330;
    
    /// <inheritdoc />
    public override byte ReadByte(int port) {
        UpdateLastPortRead(port);
        return port switch {
            DataPort => _dataBytes.Count > 0 ? _dataBytes.Dequeue() : (byte)0,
            StatusPort => (byte)(~(byte)Status & 0xC0),
            _ => base.ReadByte(port)
        };
    }

    /// <inheritdoc />
    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(Data, this);
        ioPortDispatcher.AddIOPortHandler(Command, this);
    }

    /// <inheritdoc />
    public override void WriteByte(int port, byte value) {
        UpdateLastPortWrite(port, value);
        switch (port) {
            case DataPort:
                _midiMapper.SendByte(value);
                break;

            case StatusPort:
                switch (value) {
                    case ResetCommand:
                        State = GeneralMidiState.NormalMode;
                        _dataBytes.Clear();
                        _dataBytes.Enqueue(CommandAcknowledge);
                        break;

                    case EnterUartModeCommand:
                        State = GeneralMidiState.UartMode;
                        _dataBytes.Enqueue(CommandAcknowledge);
                        break;
                }
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }
    
    private void Dispose(bool disposing) {
        if(!_disposed) {
            if(disposing) {
                _midiMapper.Dispose();
            }
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
    }
}