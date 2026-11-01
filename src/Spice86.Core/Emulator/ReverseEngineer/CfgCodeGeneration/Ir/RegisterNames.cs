namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

/// <summary>The C# names of the registers, indexed by <c>RegisterIndex</c> /
/// <c>SegmentRegisterIndex</c>: the only copy of the two tables, in lowercase. A consumer that
/// prints a capitalized name derives it with <see cref="PascalOf"/>.</summary>
internal static class RegisterNames {
    /// <summary>The 16-bit register names, in register-index order.</summary>
    public static readonly string[] Word16 = ["ax", "cx", "dx", "bx", "sp", "bp", "si", "di"];

    /// <summary>The segment register names, in segment-register-index order.</summary>
    public static readonly string[] Segments = ["es", "cs", "ss", "ds", "fs", "gs"];

    /// <summary><paramref name="name"/> with an uppercase first letter (<c>"ax"</c> to
    /// <c>"Ax"</c>), for the declarations that print capitalized names.</summary>
    public static string PascalOf(string name) =>
        string.Concat(name[..1].ToUpperInvariant(), name.AsSpan(1));
}
