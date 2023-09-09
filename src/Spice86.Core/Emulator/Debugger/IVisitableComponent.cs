namespace Spice86.Core.Emulator.Debugger;

/// <summary>
/// Interface implemented by internal Emulator components that can be visited
/// </summary>
public interface IVisitableComponent {
    /// <summary>
    /// Lets the visitor enter and visit the class
    /// </summary>
    /// <param name="emulatorVisitor">The class that will accumulate data about the visited class</param>
    void Accept(IEmulatorVisitor emulatorVisitor);
}