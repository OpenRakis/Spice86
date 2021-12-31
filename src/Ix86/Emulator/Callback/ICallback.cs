namespace Ix86.Emulator.Callback;

using Ix86.Utils;

public interface ICallback : ICheckedRunnable
{
    public int GetIndex();
}
