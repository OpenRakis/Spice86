namespace Spice86.Core.Emulator.Debugger;

/// <summary>
/// A class that visits another class that lives inside the Emulator
/// </summary>
public interface IEmulatorVisitor {
    void Visit<T>(T visitable) where T : IVisitableComponent;
}