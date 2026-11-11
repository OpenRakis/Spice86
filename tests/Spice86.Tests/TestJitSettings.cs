namespace Spice86.Tests;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

/// <summary>JIT settings read from the environment, shared by every test that compiles CFG nodes.</summary>
internal static class TestJitSettings {
    /// <summary>Environment variable that turns <see cref="GeneratorIr"/> on when set to <c>1</c>.</summary>
    public const string GeneratorIrVariable = "SPICE86_JIT_GENERATOR_IR";

    /// <summary>
    /// True when <see cref="GeneratorIrVariable"/> is <c>1</c>: the JIT then executes the execution AST that
    /// the IR round trip of the code generator rebuilt instead of the parsed one.
    /// </summary>
    public static bool GeneratorIr { get; } = Environment.GetEnvironmentVariable(GeneratorIrVariable) == "1";

    /// <summary>Environment variable that turns <see cref="CompiledRun"/> on when set to <c>1</c>.</summary>
    public const string CompiledRunVariable = "SPICE86_JIT_COMPILED_RUN";

    /// <summary>
    /// True when <see cref="CompiledRunVariable"/> is <c>1</c>: every catalogue scenario execution then
    /// also runs with <see cref="JitMode.CompiledOnly"/> checked against its oracle, engine assertions
    /// and machine goldens.
    /// </summary>
    public static bool CompiledRun { get; } = Environment.GetEnvironmentVariable(CompiledRunVariable) == "1";

    /// <summary>The execution AST rewriter the compiler applies: the IR round trip when enabled.</summary>
    public static IExecutionAstRewriter Rewriter { get; } =
        GeneratorIr ? new GeneratorIrRoundTrip() : new IdentityExecutionAstRewriter();

    /// <summary>True when the compiler expands ALU calls inline: with the IR round trip.</summary>
    public static bool ExpandAluCalls { get; } = GeneratorIr;
}
