namespace Spice86.Core.Emulator.Callback;

/// <summary>
/// The base interface for classes that can be called back.
/// </summary>
public interface IRunnable {
    /// <summary>
    /// The code to invoke when calling back.
    /// </summary>
    public void Run();
}