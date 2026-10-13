namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Devices.Video.Registers.General;

internal readonly record struct VgaTimingRegisters(
    MiscellaneousOutput.ClockSelectValue ClockSelect, int DotsPerClock, bool HalfDotClock,
    byte HorizontalTotal, byte HorizontalDisplayEnd, byte HorizontalBlankingStart, byte HorizontalBlankingEnd,
    byte HorizontalSyncStart, byte HorizontalSyncEnd, int VerticalTotal, int VerticalDisplayEnd,
    int VerticalBlankingStart, byte VerticalBlankingEnd, int VerticalSyncStart, byte VerticalSyncEnd, bool VerticalTimingHalved) {
    internal static VgaTimingRegisters Read(IVideoState state) {
        CrtControllerRegisters crt = state.CrtControllerRegisters;
        return new(state.GeneralRegisters.MiscellaneousOutput.ClockSelect,
            state.SequencerRegisters.ClockingModeRegister.DotsPerClock, state.SequencerRegisters.ClockingModeRegister.HalfDotClock,
            crt.HorizontalTotal, crt.HorizontalDisplayEnd, crt.HorizontalBlankingStart, crt.HorizontalBlankingEndValue,
            crt.HorizontalSyncStart, crt.HorizontalSyncEndRegister.HorizontalSyncEnd, crt.VerticalTotalValue,
            crt.VerticalDisplayEndValue, crt.VerticalBlankingStartValue, crt.VerticalBlankingEnd,
            crt.VerticalSyncStartValue, crt.VerticalSyncEndRegister.VerticalSyncEnd, crt.CrtModeControlRegister.VerticalTimingHalved);
    }
}
