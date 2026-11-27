namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Failing-first proof for <see cref="GeneratedCodeChecks"/>: each kind, plus the program-only
/// extraction, fails with a message naming the golden key and the checked text.
/// </summary>
public sealed class GeneratedCodeChecksTest {
    private static GeneratedCSharpProgram AddProgram() =>
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(AsmScenarioCatalog.CpuBin("add"));

    /// <summary>A Contains of a string absent from the program fails naming key and text.</summary>
    [Fact]
    public void ContainsAbsentStringFails() {
        Action check = () => GeneratedCodeChecks.Assert("add.spec", AddProgram(),
            [new GeneratedCodeCheck(GeneratedCodeCheckKind.Contains, "a string the program never emits")]);
        check.Should().Throw<Exception>()
            .WithMessage("*add.spec*Contains*a string the program never emits*");
    }

    /// <summary>A DoesNotContain of a string the program holds fails naming key and text.</summary>
    [Fact]
    public void DoesNotContainPresentStringFails() {
        Action check = () => GeneratedCodeChecks.Assert("add.spec", AddProgram(),
            [new GeneratedCodeCheck(GeneratedCodeCheckKind.DoesNotContain, "Stack.Push16(FlagRegister16);")]);
        check.Should().Throw<Exception>()
            .WithMessage("*add.spec*DoesNotContain*Stack.Push16(FlagRegister16);*");
    }

    /// <summary>A MethodSignature whose parameter list does not match fails naming key and text.</summary>
    [Fact]
    public void MethodSignatureWrongParameterListFails() {
        Action check = () => GeneratedCodeChecks.Assert("add.spec", AddProgram(),
            [new GeneratedCodeCheck(GeneratedCodeCheckKind.MethodSignature,
                "public virtual Action unknown_F000_FFF0_FFFF0(int loadOffset) {")]);
        check.Should().Throw<Exception>()
            .WithMessage("*add.spec*MethodSignature*public virtual Action unknown_F000_FFF0_FFFF0(int loadOffset) {*");
    }

    /// <summary>A Contains of a string only a support file holds fails: the extraction keeps program
    /// files only.</summary>
    [Fact]
    public void ContainsSupportFileOnlyStringFails() {
        Action check = () => GeneratedCodeChecks.Assert("add.spec", AddProgram(),
            [new GeneratedCodeCheck(GeneratedCodeCheckKind.Contains, "DefineFunctionWithoutInterpreterFallback")]);
        check.Should().Throw<Exception>()
            .WithMessage("*add.spec*Contains*DefineFunctionWithoutInterpreterFallback*");
    }
}
