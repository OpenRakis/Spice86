namespace Spice86.Core.Emulator.InternalDebugger;

/// <summary>
/// Interface implemented by internal Emulator components that can be visited by the UI debugger.
/// </summary>
public interface IDebuggableComponent {
    /// <summary>
    /// Lets the visitor enter and read the state of the class
    /// </summary>
    /// <param name="emulatorDebugger">The class that will read the state of the component, or mutate its state.</param>
    void Accept<T>(T emulatorDebugger) where T : IInternalDebugger;
}