namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Emulator.Devices.Sound.Ym7128b;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Adlib Gold implementation
/// </summary>
public class AdlibGold : DefaultIOPortHandler {
    private YM7128B_ChipIdeal chip = new();

    private struct ControlState {
        public byte Sci { get; set; }
        public byte A0 { get; set; }
        public byte Addr { get; set; }
        public byte Data { get; set; }
    }

    private ControlState control_state = new();

    private enum StereoProcessorControlReg {
        VolumeLeft,
        VolumeRight,
        Bass,
        Treble,
        SwitchFunctions,
    }

    private enum StereoProcessorMode {
        ForcedMono,
        LineStereo,
        PseudoStereo,
        SpatialStereo
    }

    private enum StereoProcessorSourceSelector {
        SoundA1 = 2,
        SoundA2 = 3,
        SoundB1 = 4,
        SoundB2 = 5,
        Stereo1 = 6,
        Stereo2 = 7,
    }
    private class StereoProcessor {
        
    }

    public AdlibGold(Machine machine, Configuration configuration, ILoggerService loggerService) : base(machine, configuration, loggerService) {
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(OPLConsts.FM_MUSIC_DATA_PORT_NUMBER_2, this);
        ioPortDispatcher.AddIOPortHandler(OPLConsts.FM_MUSIC_STATUS_PORT_NUMBER_2, this);
    }

    public override byte ReadByte(int port) {
        return base.ReadByte(port);
    }

    public override void WriteByte(int port, byte value) {
        base.WriteByte(port, value);
    }
}