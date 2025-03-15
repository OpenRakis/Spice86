﻿namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// The class responsible for emulating the OPL FM music synth chip.
/// </summary>
public class OPLFMChip : DefaultIOPortHandler, IDisposable {
    private const byte Timer1Mask = 0xC0;
    private const byte Timer2Mask = 0xA0;

    private bool _dualOpl = false;

    private readonly SoundChannel _soundChannel;
    private readonly DeviceThread _deviceThread;
    private readonly float[] _synthReadBuffer = new float[1024];
    private readonly float[] _playBuffer = new float[1024 * 2];

    private bool _disposed;

    /// <summary>
    /// The sound channel used for rendering audio.
    /// </summary>
    public SoundChannel SoundChannel => _soundChannel;

    /// <summary>
    /// Initializes a new instance of the OPL FM synth chip.
    /// </summary>
    /// <param name="fmSynthSoundChannel">The software mixer's sound channel for the OPL FM Synth chip.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="ioPortDispatcher">The class that is responsible for dispatching ports reads and writes to classes that respond to them.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="pauseHandler">Class for handling pausing the emulator.</param>
    public OPLFMChip(SoundChannel fmSynthSoundChannel, State state, IOPortDispatcher ioPortDispatcher, bool failOnUnhandledPort, ILoggerService loggerService, IPauseHandler pauseHandler) : base(state, failOnUnhandledPort, loggerService) {
        _soundChannel = fmSynthSoundChannel;
        //_synth = new(48000);
        _deviceThread = new DeviceThread(nameof(OPLFMChip), PlaybackLoopBody, pauseHandler, loggerService);
        InitPortHandlers(ioPortDispatcher);
    }


    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(0x388, this);
        ioPortDispatcher.AddIOPortHandler(0x389, this);
        ioPortDispatcher.AddIOPortHandler(0x38b, this);
        if (_dualOpl) {
            //Read/Write
            ioPortDispatcher.AddIOPortHandler(0x220, this);
            //Read/Write
            ioPortDispatcher.AddIOPortHandler(0x223, this);
        }
        //Read/Write
        ioPortDispatcher.AddIOPortHandler(0x228, this);
        //Write
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
        //TOOD, from Opl::PortRead
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public override ushort ReadWord(ushort port) {
        //TODO...?
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        InitializePlaybackIfNeeded();
        //TODO, from Opl::PortWrite
    }

    private void InitializePlaybackIfNeeded() {
        if (!_deviceThread.Active) {
            _deviceThread.StartThreadIfNeeded();
        }
    }

    /// <inheritdoc />
    public override void WriteWord(ushort port, ushort value) {
       //TODO...?
    }

    /// <summary>
    /// Generates and plays back output waveform data.
    /// </summary>
    private void PlaybackLoopBody() {
        //TODO
        //_soundChannel.Render(_playBuffer);
    }

    private void MonoToStereo(Span<float> buffer, Span<float> playBuffer) {
        //_synth?.GetData(buffer);
        ChannelAdapter.MonoToStereo(buffer, playBuffer);
    }
}