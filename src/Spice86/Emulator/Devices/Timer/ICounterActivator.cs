namespace Spice86.Emulator.Devices.Timer;

internal interface ICounterActivator
{
    /// <summary>
    /// True when activation can occur.
    /// </summary>
    /// <returns></returns>
    public bool IsActive();

    public void UpdateDesiredFrequency(long desiredFrequency);
}
