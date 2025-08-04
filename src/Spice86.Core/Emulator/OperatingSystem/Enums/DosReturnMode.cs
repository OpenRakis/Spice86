namespace Spice86.Core.Emulator.OperatingSystem.Enums;
internal enum DosReturnMode : byte {
    Exit = 0,
    CtrlC = 1,
    Abort = 2,
    TerminateAndStayResident = 3
}