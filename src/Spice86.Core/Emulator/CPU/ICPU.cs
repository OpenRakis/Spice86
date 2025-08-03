namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Function;

public interface ICPU : IInstructionExecutor, IFunctionHandlerProvider {
    public Stack Stack { get; }
    public State State { get; }
    InterruptVectorTable InterruptVectorTable { get; }
    void FarRet(ushort numberOfBytesToPop);
    void InterruptRet();
    void NearRet(int numberOfBytesToPop);
}