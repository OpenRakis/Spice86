namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video.Registers.Enums;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Captures the registers the devices hold when a run ends, in a fixed order, so an interpreted run
/// and its generated run can be compared on what the program left behind rather than on the port
/// writes that produced it. Values that follow the cycle counter are left out: the PIT current
/// count, its latches and timing fields, and CMOS status register C whose interrupt flags the
/// periodic tick sets and reads clear.
/// </summary>
internal static class DeviceRegisterSnapshot {
    /// <summary>The device, register name and value of every captured register, in a fixed order.</summary>
    public static IReadOnlyList<(string Device, string Register, uint Value)> Capture(Machine machine) {
        List<(string Device, string Register, uint Value)> registers = [];
        CaptureVga(machine, registers);
        CaptureCmos(machine, registers);
        CapturePit(machine, registers);
        CaptureOpl(machine, registers);
        return registers;
    }

    private static void CaptureVga(Machine machine,
        List<(string Device, string Register, uint Value)> registers) {
        for (int i = 0; i <= (int)SequencerRegister.MemoryMode; i++) {
            registers.Add(("vga", $"sequencer[{i:X2}]",
                machine.VgaRegisters.SequencerRegisters.ReadRegister((SequencerRegister)i)));
        }
        for (int i = 0; i <= (int)GraphicsControllerRegister.BitMask; i++) {
            registers.Add(("vga", $"graphics[{i:X2}]",
                machine.VgaRegisters.GraphicsControllerRegisters.ReadRegister((GraphicsControllerRegister)i)));
        }
        for (int i = 0; i <= (int)CrtControllerRegister.LineCompare; i++) {
            registers.Add(("vga", $"crtc[{i:X2}]",
                machine.VgaRegisters.CrtControllerRegisters.ReadRegister((CrtControllerRegister)i)));
        }
        for (int i = 0; i <= (int)AttributeControllerRegister.ColorSelect; i++) {
            registers.Add(("vga", $"attribute[{i:X2}]",
                machine.VgaRegisters.AttributeControllerRegisters.ReadRegister((AttributeControllerRegister)i)));
        }
        registers.Add(("vga", "misc", machine.VgaRegisters.GeneralRegisters.MiscellaneousOutput.Value));
        byte[,] palette = machine.VgaRegisters.DacRegisters.Palette;
        for (int i = 0; i < 256; i++) {
            registers.Add(("vga", $"dac[{i:X3}].r", palette[i, 0]));
            registers.Add(("vga", $"dac[{i:X3}].g", palette[i, 1]));
            registers.Add(("vga", $"dac[{i:X3}].b", palette[i, 2]));
        }
    }

    private static void CaptureCmos(Machine machine,
        List<(string Device, string Register, uint Value)> registers) {
        CmosRegisters cmos = machine.RealTimeClock.Registers;
        for (int i = 0; i < CmosRegisters.RegisterCount; i++) {
            if (i == CmosRegisterAddresses.StatusRegisterC) {
                continue;
            }
            registers.Add(("cmos", $"cmos[{i:X2}]", cmos[i]));
        }
    }

    private static void CapturePit(Machine machine,
        List<(string Device, string Register, uint Value)> registers) {
        PitTimerState timer = machine.Timer.CaptureState();
        PitChannelState[] channels = [timer.Channel0, timer.Channel1, timer.Channel2];
        for (int channel = 0; channel < 3; channel++) {
            PitChannelState channelState = channels[channel];
            registers.Add(("pit", $"channel{channel}.mode", (byte)channelState.Mode));
            registers.Add(("pit", $"channel{channel}.bcd", channelState.Bcd ? 1u : 0u));
            registers.Add(("pit", $"channel{channel}.readaccess", (byte)channelState.ReadMode));
            registers.Add(("pit", $"channel{channel}.writeaccess", (byte)channelState.WriteMode));
            registers.Add(("pit", $"channel{channel}.reload", channelState.WriteLatch));
        }
    }

    private static void CaptureOpl(Machine machine,
        List<(string Device, string Register, uint Value)> registers) {
        IReadOnlyList<byte> cache = machine.OPL.RegisterCache;
        for (int i = 0; i < cache.Count; i++) {
            registers.Add(("opl", $"bank{i >> 8}[{i & 0xFF:X2}]", cache[i]));
        }
    }
}
