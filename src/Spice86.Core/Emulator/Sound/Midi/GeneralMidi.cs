namespace Spice86.Core.Emulator.Sound.Midi;

using Spice86.Shared.Interfaces;
using Spice86.Core.Emulator.Sound.Midi.MT32;
using System;
using Spice86.Core.Emulator.Pause;

/// <summary>
/// Virtual device which emulates General MIDI playback.
/// </summary>
public sealed class GeneralMidi : IPauseable, IDisposable {
    private readonly AudioPlayerFactory _audioPlayerFactory;
    private MidiDevice? _midiMapper;
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

    private readonly ILoggerService _loggerService;


    /// <summary>
    /// Initializes a new instance of the GeneralMidi class.
    /// </summary>
    /// <param name="audioPlayerFactory">The AudioPlayer factory.</param>
    /// <param name="mt32RomsPath">Where are the MT-32 ROMs path located.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public GeneralMidi(AudioPlayerFactory audioPlayerFactory, string? mt32RomsPath, ILoggerService loggerService) {
        _audioPlayerFactory = audioPlayerFactory;
        _loggerService = loggerService;
        Mt32RomsPath = mt32RomsPath;
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
    /// Gets or sets a value indicating whether to emulate an MT-32 device.
    /// </summary>
    public bool UseMT32 => !string.IsNullOrWhiteSpace(Mt32RomsPath);

    /// <summary>
    /// Gets the current value of the MIDI status port.
    /// </summary>
    private GeneralMidiStatus GetStatus() {
        GeneralMidiStatus status = GeneralMidiStatus.OutputReady;

        if (_dataBytes.Count > 0) {
            status |= GeneralMidiStatus.InputReady;
        }

        return status;
    }

    /// <summary>
    /// All the input ports usable with the device.
    /// </summary>
    public IEnumerable<int> InputPorts => new int[] { DataPort, StatusPort };

    /// <summary>
    /// Read a byte from a port. Either the Data port or the Status port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <returns>The value from the port.</returns>
    /// <exception cref="ArgumentException">When the port isn't recognized.</exception>
    public byte ReadByte(int port) {
        switch (port) {
            case DataPort:
                if (_dataBytes.Count > 0) {
                    return _dataBytes.Dequeue();
                } else {
                    return 0;
                }

            case StatusPort:
                return (byte)(~(byte)GetStatus() & 0xC0);

            default:
                throw new ArgumentException("Invalid MIDI port.");
        }
    }

    /// <summary>
    /// Read a byte from a port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <returns>The value from the port.</returns>
    /// <exception cref="ArgumentException">When the port isn't recognized.</exception>
    public ushort ReadWord(int port) => ReadByte(port);

    /// <summary>
    /// All the output ports usable with the device.
    /// </summary>
    public IEnumerable<int> OutputPorts => new int[] { 0x330, 0x331 };

    /// <summary>
    /// Gets or sets whether the General MIDI render thread is paused
    /// </summary>
    public bool IsPaused {
        get => _midiMapper?.IsPaused is true or null;
        set {
            if(_midiMapper is not null) {
                _midiMapper.IsPaused = value;
            }
        }
    }

    /// <summary>
    /// Writes a byte to the specified port, either the DataPort or StatusPort. <br/>
    /// If the DataPort is specified, the byte is sent to the MIDI device through the MIDI mapper. <br/>
    /// If the StatusPort is specified, the byte is interpreted as a command, and the GeneralMidiState may be modified accordingly.
    /// </summary>
    /// <param name="port">The port to write the byte to, either DataPort or StatusPort.</param>
    /// <param name="value">The byte value to write.</param>
    public void WriteByte(int port, byte value) {
        switch (port) {
            case DataPort:
                if (_midiMapper is null) {
                    if (UseMT32 && !string.IsNullOrWhiteSpace(Mt32RomsPath)) {
                        _midiMapper = new Mt32MidiDevice(_audioPlayerFactory, Mt32RomsPath, _loggerService);
                    } else {
                        _midiMapper = new GeneralMidiDevice(_audioPlayerFactory);
                    }
                }

                _midiMapper?.SendByte(value);
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
        }
    }

    /// <summary>
    /// Write a word to the device.
    /// </summary>
    /// <param name="port">The port to write to.</param>
    /// <param name="value">The value being written.</param>
    public void WriteWord(int port, ushort value) => WriteByte(port, (byte)value);

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if(!_disposed) {
            if(disposing) {
                _midiMapper?.Dispose();
                _midiMapper = null;
            }
            _disposed = true;
        }
    }
}