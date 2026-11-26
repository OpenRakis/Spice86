namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Naming;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// The generic names of generated values: a value is never named after its register but after
/// its width: <c>byte</c> for <c>byte</c>, <c>word</c> for <c>ushort</c>, <c>dword</c> for
/// <c>uint</c>, <c>flag</c> for <c>bool</c>. Inside one scope each name is the prefix plus a counter
/// starting at 1, in the order the values are numbered (<c>byte1</c>, <c>word2</c>).
/// </summary>
internal static class GenericValueNames {
    /// <summary>The name prefix of a value of <paramref name="width"/>.</summary>
    public static string Prefix(BitWidth width) => width switch {
        BitWidth.BYTE_8 => "byte",
        BitWidth.WORD_16 => "word",
        _ => "dword"
    };

    /// <summary>The name prefix of a parameter or output whose C# type is <paramref name="type"/>.</summary>
    public static string Prefix(string type) => type switch {
        "byte" or "sbyte" => "byte",
        "bool" => "flag",
        "uint" or "int" => "dword",
        _ => "word"
    };

    /// <summary>The first name of <paramref name="prefix"/> free in <paramref name="taken"/>,
    /// counting up from 1.</summary>
    public static string Next(string prefix, IReadOnlySet<string> taken) {
        int number = 0;
        string name;
        do {
            name = $"{prefix}{++number}";
        } while (taken.Contains(name));
        return name;
    }
}
