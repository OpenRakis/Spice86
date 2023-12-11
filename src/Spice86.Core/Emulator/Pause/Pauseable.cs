namespace Spice86.Core.Emulator.Pause;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Repreents a class that can pause its internal render or emulation thread. <br/>
/// Used primarly by the <see cref="EmulationLoop"/> and classes such as the <see cref="SoundBlaster"/>
/// </summary>
public class Pauseable : IPauseable {
    /// <inheritdoc/>
    public virtual bool IsPaused { get; set; }

    /// <summary>
    /// Sleep while the <see cref="IsPaused"/> is <c>true</c>
    /// </summary>
    public void SleepWhilePaused() {
        while (IsPaused) {
            Thread.Sleep(1);
        }
    }
}
