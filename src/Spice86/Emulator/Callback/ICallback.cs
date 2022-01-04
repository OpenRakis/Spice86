namespace Spice86.Emulator.Callback;

using System;

public interface ICallback<T>
{
    public int GetIndex();

    public Func<T> GetCallback();
}
