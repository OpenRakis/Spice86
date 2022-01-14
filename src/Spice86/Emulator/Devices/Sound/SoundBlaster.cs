namespace Spice86.Emulator.Devices.Sound;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.Machine;

/// <summary>
/// Sound blaster implementation. Emulates an absent card :) http://www.fysnet.net/detectsb.htm
/// </summary>
public class SoundBlaster : DefaultIOPortHandler
{
    private static readonly int DSP_DATA_AVAILABLE_PORT_NUMBER = 0x22E;
    private static readonly int DSP_READ_PORT_NUMBER = 0x22A;
    private static readonly int DSP_RESET_PORT_NUMBER = 0x226;
    private static readonly int DSP_WRITE_BUFFER_STATUS_PORT_NUMBER = 0x22C;
    private static readonly int FM_MUSIC_DATA_PORT_NUMBER = 0x229;
    private static readonly int FM_MUSIC_DATA_PORT_NUMBER_2 = 0x389;
    private static readonly int FM_MUSIC_STATUS_PORT_NUMBER = 0x228;
    private static readonly int FM_MUSIC_STATUS_PORT_NUMBER_2 = 0x388;
    private static readonly int LEFT_SPEAKER_DATA_PORT_NUMBER = 0x221;
    private static readonly int LEFT_SPEAKER_STATUS_PORT_NUMBER = 0x220;
    private static readonly int MIXER_DATA_PORT_NUMBER = 0x225;
    private static readonly int MIXER_REGISTER_PORT_NUMBER = 0x224;
    private static readonly int RIGHT_SPEAKER_DATA_PORT_NUMBER = 0x223;
    private static readonly int RIGHT_SPEAKER_STATUS_PORT_NUMBER = 0x222;

    public SoundBlaster(Machine machine, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort)
    {
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher)
    {
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