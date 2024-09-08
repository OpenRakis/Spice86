namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Virtual device which emulates OPL2 FM music.
/// </summary>
public class OPL2FM : DefaultIOPortHandler, IDisposable {
    private readonly SoundChannel _soundChannel;
    private readonly DeviceThread _deviceThread;
    private readonly float[] _playBuffer = new float[1024 * 2];

    private bool _disposed;

    /// <summary>
    /// The sound channel used for the OPL2 FM synth chip.
    /// </summary>
    public SoundChannel SoundChannel => _soundChannel;

    /// <summary>
    /// Initializes a new instance of the OPL2 FM synth chip.
    /// </summary>
    /// <param name="fmSynthSoundChannel">The software mixer's sound channel for the OPL2 FM Synth chip.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="ioPortDispatcher">The class that is responsible for dispatching ports reads and writes to classes that respond to them.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="pauseHandler">Class for handling pausing the emulator.</param>
    public OPL2FM(SoundChannel fmSynthSoundChannel, State state,
        IOPortDispatcher ioPortDispatcher, bool failOnUnhandledPort,
        ILoggerService loggerService, IPauseHandler pauseHandler)
        : base(state, failOnUnhandledPort, loggerService) {
        _soundChannel = fmSynthSoundChannel;
        _deviceThread = new DeviceThread(nameof(OPL2FM), PlaybackLoopBody,
            pauseHandler, loggerService);
        InitPortHandlers(ioPortDispatcher);
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(0x388, this);
        ioPortDispatcher.AddIOPortHandler(0x389, this);
        ioPortDispatcher.AddIOPortHandler(0x228, this);
        ioPortDispatcher.AddIOPortHandler(0x229, this);
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if(!_disposed) {
            if(disposing) {
                _deviceThread.Dispose();
            }
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        return 0;
    }

    /// <inheritdoc />
    public override ushort ReadWord(ushort port) {
        return 0;
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        _deviceThread.StartThreadIfNeeded();

    }

    /// <inheritdoc />
    public override void WriteWord(ushort port, ushort value) {
        _deviceThread.StartThreadIfNeeded();

    }

    /// <summary>
    /// Generates and plays back output waveform data.
    /// </summary>
    private void PlaybackLoopBody() {
        FillBuffer(_playBuffer);
        _soundChannel.Render(_playBuffer);
    }

    private void FillBuffer(Span<float> playBuffer) {
    }
}