namespace Spice86.Core.Emulator.InternalDebugger;
public interface IInternalDebugger
{
    void Visit<T>(T component) where T : IDebuggableComponent;
}
