namespace Spice86.Emulator.ReverseEngineer;

using Spice86.Utils;

public class CheckedSupplier<T> : ICheckedSupplier<T>
{
    private readonly T _runnable;

    public CheckedSupplier(T runnable)
    {
        _runnable = runnable;
    }

    T ICheckedSupplier<T>.Get()
    {
        return _runnable;
    }
}
