namespace Spice86.Emulator.Callback;

using Spice86.Utils;

public interface ICallback : ICheckedRunnable
{
    public int GetIndex();
}
