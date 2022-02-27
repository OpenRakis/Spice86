namespace Spice86.Emulator.Devices.Sound;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.VM;

/// <summary>
/// Sound blaster implementation. Emulates an absent card :) http://www.fysnet.net/detectsb.htm
/// </summary>
public class SoundBlaster : DefaultIOPortHandler {
    private const int DSP_DATA_AVAILABLE_PORT_NUMBER = 0x22E;
    private const int DSP_READ_PORT_NUMBER = 0x22A;
    private const int DSP_RESET_PORT_NUMBER = 0x226;
    private const int DSP_WRITE_BUFFER_STATUS_PORT_NUMBER = 0x22C;
    private const int FM_MUSIC_DATA_PORT_NUMBER = 0x229;
    private const int FM_MUSIC_DATA_PORT_NUMBER_2 = 0x389;
    private const int FM_MUSIC_STATUS_PORT_NUMBER = 0x228;
    private const int FM_MUSIC_STATUS_PORT_NUMBER_2 = 0x388;
    private const int LEFT_SPEAKER_DATA_PORT_NUMBER = 0x221;
    private const int LEFT_SPEAKER_STATUS_PORT_NUMBER = 0x220;
    private const int MIXER_DATA_PORT_NUMBER = 0x225;
    private const int MIXER_REGISTER_PORT_NUMBER = 0x224;
    private const int RIGHT_SPEAKER_DATA_PORT_NUMBER = 0x223;
    private const int RIGHT_SPEAKER_STATUS_PORT_NUMBER = 0x222;

    public SoundBlaster(Machine machine, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort) {

    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(LEFT_SPEAKER_STATUS_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(LEFT_SPEAKER_DATA_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(RIGHT_SPEAKER_STATUS_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(RIGHT_SPEAKER_DATA_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(MIXER_REGISTER_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(MIXER_DATA_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(DSP_RESET_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(FM_MUSIC_STATUS_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(FM_MUSIC_STATUS_PORT_NUMBER_2, this);
        ioPortDispatcher.AddIOPortHandler(FM_MUSIC_DATA_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(FM_MUSIC_DATA_PORT_NUMBER_2, this);
        ioPortDispatcher.AddIOPortHandler(DSP_READ_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(DSP_WRITE_BUFFER_STATUS_PORT_NUMBER, this);
        ioPortDispatcher.AddIOPortHandler(DSP_DATA_AVAILABLE_PORT_NUMBER, this);
    }
}