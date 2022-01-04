namespace Spice86.Emulator.Callback;

using System;

public interface ICallback<T>
{
    public Func<T> GetCallback();

    public int GetIndex();
}