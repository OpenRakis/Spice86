namespace Spice86.Core.Emulator.Devices.Sound.PCSpeaker;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Emulates a PC Speaker with both discrete sampling and impulse response capabilities.
/// </summary>
public sealed class PcSpeaker : DefaultIOPortHandler, IDisposable {
    private const int PcSpeakerPortNumber = 0x61;
    private bool _disposed;
    private readonly DeviceThread _deviceThread;
    private readonly Pit8254Counter _pit8254Counter;
    private readonly SoundChannel _soundChannel;

    /// <summary>
    /// Initializes a new instance of the <see cref="PcSpeaker"/> class.
    /// </summary>
    public PcSpeaker(SoftwareMixer softwareMixer,
        State state,
        Pit8254Counter pit8254Counter,
        IOPortDispatcher ioPortDispatcher,
        IPauseHandler pauseHandler,
        ILoggerService loggerService,
        bool failOnUnhandledPort) : base(state, failOnUnhandledPort, loggerService) {
        _deviceThread = new DeviceThread(nameof(PcSpeaker), PlaybackLoop, pauseHandler, loggerService);
        _pit8254Counter = pit8254Counter;
        ioPortDispatcher.AddIOPortHandler(PcSpeakerPortNumber, this);
        _soundChannel = softwareMixer.CreateChannel(nameof(PcSpeaker));
    }

    private void PlaybackLoop() {
        //Here, eventually, render in the sound channel
    }

    public override byte ReadByte(ushort port) {
        return base.ReadByte(port);
    }

    public override void WriteByte(ushort port, byte value) {
        _deviceThread.StartThreadIfNeeded();
        base.WriteByte(port, value);
    }

    /// <summary>
    /// Disposes resources used by this instance.
    /// </summary>
    public void Dispose() {
        Dispose(disposing: true);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _deviceThread.Dispose();
            }
            _disposed = true;
        }
    }
}