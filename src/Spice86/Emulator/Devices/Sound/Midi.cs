namespace Spice86.Emulator.Devices.Sound;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.Sound.Midi;
using Spice86.Emulator.VM;

using System;

/// <summary>
/// MPU401 (Midi) implementation. Emulates an absent card :)
/// </summary>
public class Midi : DefaultIOPortHandler {
    private const int Command = 0x331;
    private const int Data = 0x330;

    private GeneralMidi _generalMidi;

    public Midi(Machine machine, Configuration configuration) : base(machine, configuration) {
        _machine = machine;
        _generalMidi = new GeneralMidi(configuration.Mt32RomsPath);
        _machine.Paused += Machine_Paused;
        _machine.Resumed += Machine_Resumed;
    }

    private void Machine_Resumed(object? sender, EventArgs e) {
        ((IInputPort)_generalMidi).Resume();
    }

    private void Machine_Paused(object? sender, System.EventArgs e) {
        ((IInputPort)_generalMidi).Pause();
    }

    public override byte ReadByte(int port) {
        return ((IInputPort)_generalMidi).ReadByte(port);
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(Data, this);
        ioPortDispatcher.AddIOPortHandler(Command, this);
    }

    public override void WriteByte(int port, byte value) {
        ((IOutputPort)_generalMidi).WriteByte(port, value);
    }
}