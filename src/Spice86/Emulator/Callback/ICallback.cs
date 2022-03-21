namespace Spice86.Emulator.Callback;

public interface ICallback : IRunnable {

    public byte Index { get; }
    public void RunFromOverriden();
}