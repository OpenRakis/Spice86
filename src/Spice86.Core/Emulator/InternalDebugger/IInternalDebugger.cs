namespace Spice86.Core.Emulator.InternalDebugger;

/// <summary>
/// Interface for the internal debuggers implemented by the UI ViewModels.
/// </summary>
public interface IInternalDebugger {
    /// <summary>
    /// Visit an emulator component that accepts the internal debugger.
    /// </summary>
    /// <param name="component">The emulator component that accepts the internal debugger</param>
    /// <typeparam name="T">A class that implements the <see cref="IDebuggableComponent"/> interface.</typeparam>
    void Visit<T>(T component) where T : IDebuggableComponent;
    
    /// <summary>
    /// Tells if the ViewModel for the internal debugger needs to visit the emulator. Either to get references to internal objects or refresh UI data.
    /// </summary>
    public bool NeedsToVisitEmulator { get; }
}
