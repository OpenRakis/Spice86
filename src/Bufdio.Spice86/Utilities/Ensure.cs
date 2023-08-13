namespace Bufdio.Spice86.Utilities;

using System;
using System.Diagnostics;

[DebuggerStepThrough]
internal static class Ensure
{
    public static void That<TException>(bool condition, string? message = null) where TException : Exception
    {
        if (!condition)
        {
            throw string.IsNullOrWhiteSpace(message)
                ? Activator.CreateInstance<TException>()
                : (TException)Activator.CreateInstance(typeof(TException), message)!;
        }
    }
}
