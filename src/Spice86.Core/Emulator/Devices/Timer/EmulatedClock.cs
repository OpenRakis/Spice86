namespace Spice86.Core.Emulator.Devices.Timer;

using Spice86.Core.Emulator.Devices.ExternalInput;

/// <summary>
/// Clock that advances based on emulated time (emulation loop ticks) rather than wall-clock time.
/// Used when InstructionsPerSecond is configured to ensure timing consistency in headless mode.
/// </summary>
public sealed class EmulatedClock : IWallClock {
    private readonly DualPic _dualPic;
    private readonly DateTime _systemStartTime;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="EmulatedClock"/> class.
    /// </summary>
    /// <param name="dualPic">The dual PIC providing tick count for emulated time.</param>
    /// <param name="systemStartTime">The UTC timestamp when the emulator started.</param>
    public EmulatedClock(DualPic dualPic, DateTime systemStartTime) {
        _dualPic = dualPic;
        _systemStartTime = systemStartTime;
    }
    
    private static int _callCount = 0;
    
    /// <summary>
    /// Gets the current emulated time based on PIC tick count.
    /// Assumes 18.2 Hz tick rate (standard BIOS timer frequency).
    /// </summary>
    public DateTime UtcNow {
        get {
            // Convert PIC ticks to seconds (18.2 ticks per second)
            double elapsedSeconds = _dualPic.Ticks / 18.2;
            DateTime result = _systemStartTime.AddSeconds(elapsedSeconds);
            
            _callCount++;
            if (_callCount % 1000 == 0 || _callCount < 10) {
                Console.WriteLine($"EmulatedClock.UtcNow called {_callCount} times: Ticks={_dualPic.Ticks}, ElapsedSeconds={elapsedSeconds:F2}");
            }
            
            return result;
        }
    }
}
