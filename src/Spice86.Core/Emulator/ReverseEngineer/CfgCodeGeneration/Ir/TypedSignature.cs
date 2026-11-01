namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.Contracts;

using System.Linq;

/// <summary>The typed signature of a single-entry method that always returns the same way:
/// parameters for its inputs, a return value for its outputs.</summary>
internal sealed class TypedSignature {
    /// <summary>The parameters, one per input, in <see cref="IrCallingConventions"/> describe order.</summary>
    public required IReadOnlyList<TypedParameter> Parameters { get; init; }

    /// <summary>The outputs, one per changed value callers read, in describe order.</summary>
    public required IReadOnlyList<TypedParameter> Outputs { get; init; }

    /// <summary>Whether every return of the method is near or far.</summary>
    public required CallDistance Distance { get; init; }

    /// <summary>The bytes every return pops after the return address (<c>ret n</c> / <c>retf n</c>).</summary>
    public required int PoppedBytes { get; init; }

    /// <summary>The IR block the method's single entry maps to.</summary>
    public required IrBlock EntryBlock { get; init; }

    /// <summary>The C# return type: <c>void</c>, the single output's type, or a named tuple.</summary>
    public string ReturnType => Outputs.Count switch {
        0 => "void",
        1 => Outputs[0].Type,
        _ => $"({string.Join(", ", Outputs.Select(output => $"{output.Type} {output.Name}"))})"
    };

    /// <summary>The parameter list of the signature, <c>ushort ax, byte al, ...</c>.</summary>
    public string ParameterList =>
        string.Join(", ", Parameters.Select(parameter => $"{parameter.Type} {parameter.Name}"));

    /// <summary>The argument list of the wrapper and of typed call sites: the parameters' registers,
    /// <c>AX, AL, CarryFlag, ...</c>.</summary>
    public string ArgumentList => string.Join(", ", Parameters.Select(parameter => parameter.Register));

    /// <summary>The text a typed <c>ret</c> prints after its <c>NearReturn</c> / <c>FarReturn</c>:
    /// <c>return;</c>, <c>return AX;</c> or <c>return (AX, DX);</c>.</summary>
    public string ReturnStatement => Outputs.Count switch {
        0 => "return;",
        1 => $"return {Outputs[0].Register};",
        _ => $"return ({string.Join(", ", Outputs.Select(output => output.Register))});"
    };
}
