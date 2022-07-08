namespace Spice86.Emulator.Sound.Midi;

using Spice86.Emulator.Sound.Midi.MT32;

using System;
using System.Collections.Generic;

/// <summary>
/// Virtual device which emulates general midi playback.
/// </summary>
public sealed class GeneralMidi {
    private MidiDevice? _midiMapper;
    private readonly Queue<byte> _dataBytes = new();

    private const int DataPort = 0x330;
    private const int StatusPort = 0x331;
    private const byte ResetCommand = 0xFF;
    private const byte EnterUartModeCommand = 0x3F;
    private const byte CommandAcknowledge = 0xFE;

    private Configuration Configuration { get; init; }

    /// <summary>
    /// Initializes a new instance of the GeneralMidi class.
    /// </summary>
    public GeneralMidi(Configuration configuration, string? mt32RomsPath = null) {
        Mt32RomsPath = mt32RomsPath;
        Configuration = configuration;
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
    private GeneralMidiStatus Status {
        get {
            GeneralMidiStatus status = GeneralMidiStatus.OutputReady;

            if (_dataBytes.Count > 0) {
                status |= GeneralMidiStatus.InputReady;
            }

            return status;
        }
    }

    public IEnumerable<int> InputPorts => new int[] { DataPort, StatusPort };
    public byte ReadByte(int port) {
        switch (port) {
            case DataPort:
                if (_dataBytes.Count > 0) {
                    return _dataBytes.Dequeue();
                } else {
                    return 0;
                }

            case StatusPort:
                return (byte)(~(byte)Status & 0xC0);

            default:
                throw new ArgumentException("Invalid MIDI port.");
        }
    }
    public ushort ReadWord(int port) => this.ReadByte(port);

    public IEnumerable<int> OutputPorts => new int[] { 0x330, 0x331 };
    public void WriteByte(int port, byte value) {
        switch (port) {
            case DataPort:
                if (_midiMapper is null) {
                    if (UseMT32 && !string.IsNullOrWhiteSpace(Mt32RomsPath)) {
                        _midiMapper = new Mt32MidiDevice(this.Mt32RomsPath, Configuration);
                    } else if(OperatingSystem.IsWindows()) {
                        _midiMapper = new WindowsMidiMapper();
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
    public void WriteWord(int port, ushort value) => this.WriteByte(port, (byte)value);

    public void Pause() {
        _midiMapper?.Pause();
    }
    public void Resume() {
        _midiMapper?.Resume();
    }
    public void Dispose() {
        _midiMapper?.Dispose();
        _midiMapper = null;
    }

    [Flags]
    private enum GeneralMidiStatus : byte {
        /// <summary>
        /// The status of the device is unknown.
        /// </summary>
        None = 0,
        /// <summary>
        /// The command port may be written to.
        /// </summary>
        OutputReady = 1 << 6,
        /// <summary>
        /// The data port may be read from.
        /// </summary>
        InputReady = 1 << 7
    }
}

/// <summary>
/// Specifies the current state of the General MIDI device.
/// </summary>
public enum GeneralMidiState {
    /// <summary>
    /// The device is in normal mode.
    /// </summary>
    NormalMode,
    /// <summary>
    /// The device is in UART mode.
    /// </summary>
    UartMode
}
