namespace Spice86.Core.Emulator.Callback;
public interface ICallback : IRunnable {
    public byte Index { get; }
    public void RunFromOverriden();
}