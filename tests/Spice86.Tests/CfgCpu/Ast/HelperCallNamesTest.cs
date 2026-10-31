namespace Spice86.Tests.CfgCpu.Ast;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Every <see cref="AluOperation"/>, <see cref="StackOperation"/> and <see cref="PortDirection"/> at every
/// width <see cref="HelperCallNames"/> can produce must name an existing public helper method: the typed
/// call nodes only carry the enum and the width, so a missing mapping would only surface when the
/// generated code fails to compile.
/// </summary>
public class HelperCallNamesTest {
    private static readonly BitWidth[] AluWidths = [BitWidth.BYTE_8, BitWidth.WORD_16, BitWidth.DWORD_32];
    private static readonly BitWidth[] StackWidths = [BitWidth.WORD_16, BitWidth.DWORD_32];
    private static readonly BitWidth[] PortWidths = [BitWidth.BYTE_8, BitWidth.WORD_16, BitWidth.DWORD_32];

    private static Type AluType(BitWidth width) => width switch {
        BitWidth.BYTE_8 => typeof(Alu8),
        BitWidth.WORD_16 => typeof(Alu16),
        BitWidth.DWORD_32 => typeof(Alu32),
        _ => throw new ArgumentOutOfRangeException(nameof(width), width, null)
    };

    [Fact]
    public void EveryAluOperationMapsToAnAluMethodAtEveryWidth() {
        foreach (AluOperation operation in Enum.GetValues<AluOperation>()) {
            foreach (BitWidth width in AluWidths) {
                AluType(width).GetMethods().Select(method => method.Name)
                    .Should().Contain(HelperCallNames.AluMethodName(operation),
                        $"{nameof(AluType)}({width}).{operation} must exist");
            }
        }
    }

    [Fact]
    public void EveryStackOperationMapsToAStackMethodAtEveryWidth() {
        foreach (StackOperation operation in Enum.GetValues<StackOperation>()) {
            foreach (BitWidth width in StackWidths) {
                typeof(Stack).GetMethods().Select(method => method.Name)
                    .Should().Contain(HelperCallNames.StackMethodName(operation, width),
                        $"Stack.{operation}{width} must exist");
            }
        }
    }

    [Fact]
    public void EveryPortDirectionMapsToAHelperMethodAtEveryWidth() {
        foreach (PortDirection direction in Enum.GetValues<PortDirection>()) {
            foreach (BitWidth width in PortWidths) {
                typeof(InstructionExecutionHelper).GetMethods().Select(method => method.Name)
                    .Should().Contain(HelperCallNames.PortMethodName(direction, width),
                        $"InstructionExecutionHelper.{direction}{width} must exist");
            }
        }
    }

    [Fact]
    public void BoundCheckMapsToAHelperMethod() {
        typeof(InstructionExecutionHelper).GetMethods().Select(method => method.Name)
            .Should().Contain(HelperCallNames.BoundCheckMethodName);
    }

    [Fact]
    public void EveryBitScanDirectionMapsToAHelperMethodAtEveryWidth() {
        foreach (BitScanDirection direction in Enum.GetValues<BitScanDirection>()) {
            foreach (BitWidth width in StackWidths) {
                typeof(InstructionExecutionHelper).GetMethods().Select(method => method.Name)
                    .Should().Contain(HelperCallNames.BitScanMethodName(direction, width),
                        $"InstructionExecutionHelper.BitScan{direction}{(int)width} must exist");
            }
        }
    }

    [Fact]
    public void EveryCallDistanceMapsToACallAndReturnHelperAtEveryWidth() {
        string[] helperNames = typeof(CSharpOverrideHelper).GetMethods().Select(method => method.Name).ToArray();
        foreach (CallDistance distance in Enum.GetValues<CallDistance>()) {
            foreach (BitWidth width in StackWidths) {
                helperNames.Should().Contain(HelperCallNames.ProcedureCallMethodName(distance, width),
                    $"CSharpOverrideHelper.{distance}Call{(int)width} must exist");
                helperNames.Should().Contain(HelperCallNames.ProcedureReturnMethodName(distance, width),
                    $"CSharpOverrideHelper.{distance}Ret{(int)width} must exist");
            }
        }
    }

    [Fact]
    public void FixedHelperNamesMapToHelperMethods() {
        string[] helperNames = typeof(CSharpOverrideHelper).GetMethods().Select(method => method.Name).ToArray();
        helperNames.Should().Contain([
            HelperCallNames.HltMethodName,
            HelperCallNames.InterruptCallMethodName,
            HelperCallNames.CallbackMethodName,
            HelperCallNames.CallbackMovedCsIpMethodName,
            HelperCallNames.SignatureMatchesMethodName
        ]);
    }
}
