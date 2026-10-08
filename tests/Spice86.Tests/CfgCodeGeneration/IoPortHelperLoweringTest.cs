namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;

using System.Collections.Generic;
using System.Linq;

using Xunit;

/// <summary>
/// Locks that every IN/OUT and INS/OUTS instruction form lowers to a call of the matching
/// <c>In8</c>...<c>Out32</c> helper on <c>CSharpOverrideHelper</c>, with a bare identifier call and no
/// reach for <c>Machine.IoPortDispatcher</c>.
/// </summary>
public class IoPortHelperLoweringTest {
    public static TheoryData<byte[], string> IoForms => new() {
        { [0xE4, 0x20], "In8" },
        { [0xE5, 0x20], "In16" },
        { [0x66, 0xE5, 0x20], "In32" },
        { [0xEC], "In8" },
        { [0xED], "In16" },
        { [0x66, 0xED], "In32" },
        { [0xE6, 0x43], "Out8" },
        { [0xE7, 0x43], "Out16" },
        { [0x66, 0xE7, 0x43], "Out32" },
        { [0xEE], "Out8" },
        { [0xEF], "Out16" },
        { [0x66, 0xEF], "Out32" },
        { [0x6C], "In8" },
        { [0x6D], "In16" },
        { [0x66, 0x6D], "In32" },
        { [0x6E], "Out8" },
        { [0x6F], "Out16" },
        { [0x66, 0x6F], "Out32" }
    };

    [Theory]
    [MemberData(nameof(IoForms))]
    public void ParsedIoFormsCompileToOneBareHelperInvocation(byte[] bytes, string expectedHelper) {
        SyntaxNode root = GenerateAndCompile(bytes);

        List<InvocationExpressionSyntax> helperInvocations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is IdentifierNameSyntax identifier
                && identifier.Identifier.ValueText == expectedHelper)
            .ToList();
        helperInvocations.Should().HaveCount(1,
            "the body must call the {0} helper exactly once through a bare identifier", expectedHelper);

        InvocationExpressionSyntax invocation = helperInvocations[0];
        int expectedArgumentCount = expectedHelper.StartsWith("In") ? 1 : 2;
        invocation.ArgumentList.Arguments.Should().HaveCount(expectedArgumentCount);

        if (expectedHelper.StartsWith("In")) {
            invocation.Parent.Should().BeOfType<AssignmentExpressionSyntax>(
                "the In* result must be assigned without a cast around the call");
            AssignmentExpressionSyntax assignment = (AssignmentExpressionSyntax)invocation.Parent;
            assignment.Right.Should().BeSameAs(invocation);
        } else {
            invocation.Parent.Should().BeOfType<ExpressionStatementSyntax>(
                "the Out* call must be a statement of its own");
        }

        root.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(identifier => identifier.Identifier.ValueText == "IoPortDispatcher")
            .Should().BeEmpty("generated code must not reach the I/O bus through the Machine property");
    }

    private static SyntaxNode GenerateAndCompile(byte[] bytes) {
        TestInstructionHelper helper = new();
        helper.State.Flags.CpuModel = CpuModel.INTEL_80386;
        CfgInstruction instruction = helper.WriteAndParse(new SegmentedAddress(0, 0), writer => {
            foreach (byte value in bytes) {
                writer.WriteUInt8(value);
            }
        });
        CfgBlock block = new(100, instruction);
        instruction.ContainingBlock = block;
        CfgCodePartition partition = new() {
            Id = 1,
            Kind = CfgCodePartitionKind.Observed,
            Name = "ioport",
            Blocks = [block],
            Entries = [
                new CfgCodePartitionEntry {
                    Node = instruction,
                    Kind = CfgCodePartitionEntryKind.FunctionEntry
                }
            ]
        };
        CfgPartitionedProgram program = new() {
            Partitions = [partition],
            Transfers = []
        };

        GeneratedCSharpProgram generatedProgram = new CfgCSharpGenerator().Generate(program);
        GeneratedOverrideCompiler compiler = new();
        GeneratedCompilation compilation = compiler.Compile(generatedProgram.SourceText);
        using CompiledGeneratedOverride compiledOverride = compiler.CompileSupplier(compilation);
        compiledOverride.Supplier.Should().NotBeNull();

        return compilation.SyntaxTree.GetRoot(TestContext.Current.CancellationToken);
    }
}
