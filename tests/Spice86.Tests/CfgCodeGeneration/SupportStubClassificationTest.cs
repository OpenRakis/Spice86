namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Which generated file holds a method: emulator-made routines the emulator wrote itself land in
/// the support-stub file, while methods inside a loaded image - including BIOS binaries and
/// emulator stubs with inline program code - stay in their segment's program file.
/// </summary>
public class SupportStubClassificationTest {
    private static string FileNameHolding(GeneratedCSharpProgram program, string methodName) {
        List<string> names = program.Files
            .Where(file => file.Text.Contains("virtual Action " + methodName, StringComparison.Ordinal))
            .Select(file => file.Name)
            .ToList();
        names.Should().ContainSingle($"exactly one file declares {methodName}");
        return names[0];
    }

    private static GeneratedCSharpProgram Generate(string generatedSourceKey) {
        using AsmRunScope scope = GeneratedScenarioCatalog.Case(generatedSourceKey).DiscoveryScenario.Prepare();
        return new GeneratedCodeMachineTestRunner().GenerateProgramAndSource(scope);
    }

    [Fact]
    public void EmulatorStubsLandInTheSupportStubFile() {
        // dos_int21h.pit.ivt installs the interrupt vector table: the F000 vectors are routines
        // the emulator wrote itself, covered by no loaded image.
        GeneratedCSharpProgram program = Generate("dos_int21h.pit.ivt.spec");

        FileNameHolding(program, "unknown_F000_0006_").Should().Be("CfgGeneratedOverrides.Support.Stubs.cs");
        FileNameHolding(program, "unknown_F000_0017_").Should().Be("CfgGeneratedOverrides.Support.Stubs.cs");
    }

    [Fact]
    public void BiosBinaryCodeStaysAProgramMethod() {
        // selfmodifyinstructions loads its BIOS binary over F000, so F000:FFF0 is program code.
        GeneratedCSharpProgram program = Generate("selfmodifyinstructions.spec");

        FileNameHolding(program, "unknown_F000_FFF0_").Should().Be("CfgGeneratedOverrides.F000.cs");
    }

    [Fact]
    public void ExecChildEntryMovesProgramCodeOutOfTheDispatchStub() {
        // child_exec_terminate_releases_handles: the EXEC child's program entry is an
        // execChildEntry partition of its own, so the DOS INT 21h dispatch stub at F000:009A
        // holds only stub code and lands in the support file while the child method stays a
        // program method.
        GeneratedCSharpProgram program = Generate("child_exec_terminate_releases_handles.a20.ivt.spec");

        FileNameHolding(program, "unknown_F000_009A_").Should().Be("CfgGeneratedOverrides.Support.Stubs.cs");
        FileNameHolding(program, "unknown_117D_0100_").Should().Be("CfgGeneratedOverrides.117D.cs");
        FileNameHolding(program, "unknown_016E_0100_").Should().Be("CfgGeneratedOverrides.016E.cs");
    }
}
