namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Captures the registers the devices hold when a run ends, in a fixed order, so an interpreted run
/// and its generated run can be compared on what the program left behind rather than on the port
/// writes that produced it. Values that follow the cycle counter are left out: CMOS status register C, whose interrupt
/// flags the periodic tick sets and reads clear. The PIT is compared through its captured state
/// instead, and so is the VGA with its registers, DAC and video memory
/// (<see cref="AsmFinalState.DeviceState"/>).
/// </summary>
internal static class DeviceRegisterSnapshot {
    /// <summary>The device, register name and value of every captured register, in a fixed order.</summary>
    public static IReadOnlyList<(string Device, string Register, uint Value)> Capture(Machine machine) {
        List<(string Device, string Register, uint Value)> registers = [];
        CaptureCmos(machine, registers);
        CaptureOpl(machine, registers);
        return registers;
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

    private static void CaptureOpl(Machine machine,
        List<(string Device, string Register, uint Value)> registers) {
        IReadOnlyList<byte> cache = machine.OPL.RegisterCache;
        for (int i = 0; i < cache.Count; i++) {
            registers.Add(("opl", $"bank{i >> 8}[{i & 0xFF:X2}]", cache[i]));
        }
    }
}
