namespace Ix86.Emulator.ReverseEngineer;

using Ix86.Utils;

using System;

public class CheckedRunnable : ICheckedRunnable
{
    private readonly Func<Action> _func;

    public CheckedRunnable(Func<Action> func)
    {
        _func = func;
    }

    public void Run()
    {
        _func.Invoke();
    }
}
