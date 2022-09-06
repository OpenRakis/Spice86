using System;
using System.Diagnostics;

namespace Bufdio.Spice86.Utilities;

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

    public static void NotNull<T>(T argument, string name) where T : class
    {
        if (argument == null)
        {
            throw new ArgumentNullException(name);
        }
    }
}
