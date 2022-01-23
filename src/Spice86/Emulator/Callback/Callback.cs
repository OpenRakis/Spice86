namespace Spice86.Emulator.Callback;

using System;

public class Callback : ICallback {
    private readonly byte _index;
    private readonly Action _runnable;

    public Callback(byte index, Action runnable) {
        _index = index;
        _runnable = runnable;
    }

    public byte GetIndex() {
        return _index;
    }

    public void Run() {
        _runnable.Invoke();
    }
}