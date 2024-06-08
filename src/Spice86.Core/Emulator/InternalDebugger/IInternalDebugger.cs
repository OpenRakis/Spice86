namespace Spice86.Core.Emulator.InternalDebugger;

/// <summary>
/// Interface for the internal debuggers implemented by the UI ViewModels.
/// </summary>
public interface IInternalDebugger
{
    /// <summary>
    /// Visit an emulator component that implements the IDebuggableComponent interface.
    /// </summary>
    /// <param name="component"></param>
    /// <typeparam name="T"></typeparam>
    void Visit<T>(T component) where T : IDebuggableComponent;
    
    /// <summary>
    /// Tells the DebugViewModel if it needs to visit the emulator.
    /// </summary>
    public bool NeedsToVisitEmulator { get; }
}
