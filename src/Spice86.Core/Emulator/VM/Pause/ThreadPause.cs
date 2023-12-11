namespace Spice86.Core.Emulator.Pause;

using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Pauses a internal render or emulation thread. <br/>
/// Used primarly by the <see cref="EmulationLoop"/> and classes such as the <see cref="SoundBlaster"/> and the <see cref="DmaController"/>
/// </summary>
public static class ThreadPause {
    /// <summary>
    /// Sleep while the condition is <c>true</c>
    /// </summary>
    public static void SleepWhilePaused(ref bool condition) {
        while (condition) {
            Thread.Sleep(1);
        }
    }
}
