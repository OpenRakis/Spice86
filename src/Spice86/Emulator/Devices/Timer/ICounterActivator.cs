namespace Spice86.Emulator.Devices.Timer;

public interface ICounterActivator {

    /// <summary> True when activation can occur. </summary>
    /// <returns> </returns>
    public bool IsActivated();

    public void UpdateDesiredFrequency(long desiredFrequency);
}