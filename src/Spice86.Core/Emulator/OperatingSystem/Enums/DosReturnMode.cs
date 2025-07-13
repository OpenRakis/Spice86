namespace Spice86.Core.Emulator.OperatingSystem.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

internal enum DosReturnMode : byte {
    Exit = 0,
    CtrlC = 1,
    Abort = 2,
    TerminateAndStayResident = 3
}
