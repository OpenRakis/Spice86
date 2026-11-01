namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>The kind of string a service reads at <c>DS:DX</c>, when it takes one.</summary>
public enum ServiceStringArgument {
    /// <summary>The service reads no string argument.</summary>
    None,
    /// <summary>The service reads a zero-terminated string.</summary>
    AsciiZ,
    /// <summary>The service reads a <c>$</c>-terminated string.</summary>
    DollarTerminated
}

/// <summary>Derived facts about a <see cref="ServiceStringArgument"/>.</summary>
public static class ServiceStringArgumentExtensions {
    /// <summary>The terminator byte of the string <paramref name="argument"/> names, or
    /// <see langword="null"/> when the service reads no string at <c>DS:DX</c>.</summary>
    public static byte? Terminator(this ServiceStringArgument argument) => argument switch {
        ServiceStringArgument.AsciiZ => 0,
        ServiceStringArgument.DollarTerminated => (byte)'$',
        _ => null
    };
}
