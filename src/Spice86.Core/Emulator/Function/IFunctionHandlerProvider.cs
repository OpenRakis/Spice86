namespace Spice86.Core.Emulator.Function;

public interface IFunctionHandlerProvider {
    public FunctionHandler FunctionHandlerInUse { get; }
    public bool IsInitialExecutionContext { get; }
}