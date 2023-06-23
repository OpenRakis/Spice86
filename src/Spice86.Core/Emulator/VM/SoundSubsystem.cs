namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Shared.Interfaces;

/// <summary>
/// Contains the PC Speaker, the external MIDI device (MT-32 or General MIDI), the FM Synth chips, and the sound cards
/// </summary>
public sealed class SoundSubsystem : IDisposable {
    private bool _disposed;
    
    /// <summary>
    /// The Gravis Ultrasound sound card. Not emulated.
    /// </summary>
    public GravisUltraSound GravisUltraSound { get; }
    
    /// <summary>
    /// The General MIDI (MPU-401) or MT-32 device.
    /// </summary>
    public Midi MidiDevice { get; }

    /// <summary>
    /// PC Speaker device.
    /// </summary>
    public PcSpeaker PcSpeaker { get; }

    /// <summary>
    /// The Sound Blaster card.
    /// </summary>
    public SoundBlaster SoundBlaster { get; }
    
    /// <summary>
    /// The OPL3 FM Synth chip.
    /// </summary>
    public OPL3FM OPL3FM { get; }
    

    public SoundSubsystem(Machine machine, Configuration configuration, ILoggerService loggerService) {
        PcSpeaker = new PcSpeaker(machine, configuration, loggerService);
        machine.RegisterIoPortHandler(PcSpeaker);
        OPL3FM = new OPL3FM(machine, configuration,  loggerService);
        machine.RegisterIoPortHandler(OPL3FM);
        SoundBlaster = new SoundBlaster(machine, configuration,  loggerService, new(7,1,5));
        machine.RegisterIoPortHandler(SoundBlaster);
        machine.DmaSubsystem.RegisterDmaDevice(SoundBlaster);
        GravisUltraSound = new GravisUltraSound(machine, configuration,  loggerService);
        machine.RegisterIoPortHandler(GravisUltraSound);
        MidiDevice = new Midi(machine, configuration,  loggerService);
        machine.RegisterIoPortHandler(MidiDevice);
    }
    
    
    /// <summary>
    /// Releases all resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                MidiDevice.Dispose();
                SoundBlaster.Dispose();
                OPL3FM.Dispose();
                PcSpeaker.Dispose();
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