namespace Spice86.Emulator.Callback;

using System;

public class Callback : ICallback
{
    private readonly Action _runnable;
    private readonly int _index;

    public Callback(int index, Action runnable)
    {
        _index = index;
        _runnable = runnable;
    }

    public int GetIndex()
    {
        return _index;
    }

    public void Run()
    {
        _runnable.Invoke();
    }
}