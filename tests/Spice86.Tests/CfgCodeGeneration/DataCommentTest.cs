namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;


using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The <c>// DOS ...</c> comments before <c>int 21h</c> calls: a comment names the service and shows
/// the dump-time bytes at <c>DS:DX</c>, only when every shown byte sits in an unchanged area of the
/// changed-memory snapshot.
/// </summary>
public class DataCommentTest {
    private const uint ImageStart = 0x20000;

    private static byte[] MemoryWith(uint offset, params byte[] bytes) {
        byte[] memory = new byte[0x40000];
        bytes.CopyTo(memory, offset);
        return memory;
    }

    private static ChangedMemorySnapshot Unchanged(uint start, uint end, params (uint Start, uint End)[] changed) =>
        new() { WatchedAreas = [(start, end)], ChangedRanges = [.. changed] };

    private static readonly SegmentedAddress DosStub = new(0xF000, 0x9000);

    private static InterruptServiceEntry DosEntry(int selectorValue, string name,
        CpuValues reads, CpuValues writes) =>
        new(selectorValue, name,
            new ServiceContract(new CallEffect(reads, writes), ServiceMemoryEffect.Read, false),
            ServiceStringArgument.None, null, []);

    private static CfgBlock[] SplitAfterInterrupt(TestInstructionHelper helper,
        (ushort Offset, byte[] Bytes)[] instructions, bool withDosStub) {
        CfgInstruction[] parsed = instructions
            .Select(instruction => Parse(helper, instruction.Offset, instruction.Bytes))
            .ToArray();
        Chain(parsed);
        int interrupt = Array.FindIndex(instructions, instruction => instruction.Bytes[0] == 0xCD);
        if (withDosStub && interrupt >= 0) {
            // The only observed target of the int site is the installed DOS stub.
            CfgInstruction stub = Parse(helper, DosStub.Offset, [0xCF]);
            parsed[interrupt].SuccessorsPerType[InstructionSuccessorType.Normal] =
                new HashSet<ICfgNode> { stub };
            parsed = [.. parsed, stub];
        }
        if (interrupt < 0 || interrupt + 1 == parsed.Length - (withDosStub ? 1 : 0)) {
            return [Block(1, parsed)];
        }
        // A call's continuation is a CallToReturn edge, not a Normal one.
        parsed[interrupt].SuccessorsPerType[InstructionSuccessorType.CallToReturn] =
            new HashSet<ICfgNode> { parsed[interrupt + 1] };
        return [Block(1, parsed[..(interrupt + 1)]), Block(2, parsed[(interrupt + 1)..])];
    }

    private static GeneratedCSharpProgram Generate(ChangedMemorySnapshot changed, byte[] memory,
        params (ushort Offset, byte[] Bytes)[] instructions) =>
        Generate(changed, memory, null, instructions);

    private static GeneratedCSharpProgram Generate(ChangedMemorySnapshot changed, byte[] memory,
        IReadOnlyDictionary<int, InterruptServiceEntry>? dosContracts,
        params (ushort Offset, byte[] Bytes)[] instructions) {
        CfgBlock[] blocks = SplitAfterInterrupt(new TestInstructionHelper(), instructions,
            dosContracts is not null);
        CfgCodePartition partition = new() {
            Id = 1,
            Kind = CfgCodePartitionKind.Observed,
            Name = "test",
            Blocks = blocks,
            Entries = SingleEntry(blocks[0])
        };
        HardwareInterruptSetup setup = GeneratorInputs.Empty.HardwareInterrupts;
        if (dosContracts is not null) {
            setup = setup with {
                InstalledInterruptHandlerAddresses = [DosStub],
                ServiceVectors = new Dictionary<byte, InterruptServiceVector> {
                    [0x21] = new InterruptServiceVector(0x21, DosStub,
                        new HandlerServices.SelectedServices(ServiceSelector.Ah, dosContracts),
                        typeof(DosInt21Handler))
                }
            };
        }
        CfgPartitionedProgram program = new() {
            Partitions = [partition],
            Transfers = [],
            Inputs = GeneratorInputs.Empty with {
                HardwareInterrupts = setup,
                ProgramEntryPoint = partition.Entries[0].Address,
                ChangedMemory = changed,
                MemorySnapshot = memory
            }
        };
        return new CfgCSharpGenerator(collectCensuses: true, verify: true).Generate(program);
    }

    private static (ushort, byte[])[] PrintCall(ushort dx) => [
        (0x0000, [0xB8, 0x00, 0x20]),   // mov ax, 0x2000
        (0x0003, [0x50]),             // push ax
        (0x0004, [0x1F]),             // pop ds
        (0x0005, [0xBA, (byte)dx, (byte)(dx >> 8)]), // mov dx, dx
        (0x0008, [0xB4, 0x09]),       // mov ah, 0x09
        (0x000A, [0xCD, 0x21]),       // int 0x21
        (0x000C, [0xC3])              // ret
    ];

    [Fact]
    public void PrintStringServiceShowsTheDollarTerminatedBytes() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0x0100, "Hi$"u8.ToArray());
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000), memory, PrintCall(0x0100));

        // Assert
        program.SourceText.Should().Contain("// DOS 09h print \"Hi$\"");
        AssertCompiles(program);
    }

    [Fact]
    public void AChangedByteDuringDiscoverySuppressesTheComment() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0x0100, "Hi$"u8.ToArray());
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000, (ImageStart + 0x0101, ImageStart + 0x0102)),
            memory, PrintCall(0x0100));

        // Assert
        program.SourceText.Should().NotContain("DOS 09h");
        AssertCompiles(program);
    }

    [Fact]
    public void AChangedTerminatorByteSuppressesTheComment() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0x0100, "Hi$"u8.ToArray());
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000, (ImageStart + 0x0102, ImageStart + 0x0103)),
            memory, PrintCall(0x0100));

        // Assert
        program.SourceText.Should().NotContain("DOS 09h");
    }

    [Fact]
    public void DataOutsideTheLoadedImagesGetsNoComment() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0x0100, "Hi$"u8.ToArray());
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart + 0x1000, ImageStart + 0x2000), memory, PrintCall(0x0100));

        // Assert
        program.SourceText.Should().NotContain("DOS 09h");
    }

    [Fact]
    public void UnknownDataOffsetGetsNoComment() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0x0100, "Hi$"u8.ToArray());
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000), memory,
            (0x0000, [0xB8, 0x00, 0x20]),   // mov ax, 0x2000
            (0x0003, [0x50]),             // push ax
            (0x0004, [0x1F]),             // pop ds
            (0x0005, [0xB4, 0x09]),       // mov ah, 0x09
            (0x0007, [0xCD, 0x21]),       // int 0x21
            (0x0009, [0xC3]));            // ret

        // Assert
        program.SourceText.Should().NotContain("DOS 09h");
        program.SourceText.Should().Contain("InterruptCall(");
    }

    [Fact]
    public void OpenServiceShowsTheAsciiZName() {
        // Arrange / Act: a non-printable byte keeps the string off the literal argument, so the
        // comment shows it escaped under the contract's name.
        IReadOnlyDictionary<int, InterruptServiceEntry> contracts = new Dictionary<int, InterruptServiceEntry> {
            [0x3D] = DosEntry(0x3D, "OpenFileOrDevice",
                CpuValues.Ah | CpuValues.Al | CpuValues.Ds | CpuValues.Dx
                | CpuValues.Ss | CpuValues.Sp,
                CpuValues.Ax | CpuValues.Carry) with { StringArgument = ServiceStringArgument.AsciiZ }
        };
        byte[] memory = MemoryWith(ImageStart + 0x0200, "DA\x01TA.BIN\0"u8.ToArray());
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000), memory, contracts,
            (0x0000, [0xB8, 0x00, 0x20]),   // mov ax, 0x2000
            (0x0003, [0x50]),             // push ax
            (0x0004, [0x1F]),             // pop ds
            (0x0005, [0xBA, 0x00, 0x02]), // mov dx, 0x0200
            (0x0008, [0xB0, 0x00]),       // mov al, 0
            (0x000A, [0xB4, 0x3D]),       // mov ah, 0x3D
            (0x000C, [0xCD, 0x21]),       // int 0x21
            (0x000E, [0xC3]));            // ret

        // Assert
        program.SourceText.Should().Contain("// DOS 3Dh OpenFileOrDevice \"DA\\x01TA.BIN\"");
        AssertCompiles(program);
    }

    [Fact]
    public void NonPrintableBytesAreEscaped() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0x0100, [0x41, 0x01, 0x24]);
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000), memory, PrintCall(0x0100));

        // Assert
        program.SourceText.Should().Contain("// DOS 09h print \"A\\x01$\"");
    }

    [Fact]
    public void QuotesAndBackslashesAreEscaped() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0x0100, "a\"b\\c$"u8.ToArray());
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000), memory, PrintCall(0x0100));

        // Assert
        program.SourceText.Should().Contain("DOS 09h print \"a\\\"b\\\\c$\"");
    }

    [Fact]
    public void APrintStringWithoutADollarIn64BytesGetsNoComment() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0x0100, new string('a', 70).Select(c => (byte)c).ToArray());
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000), memory, PrintCall(0x0100));

        // Assert
        program.SourceText.Should().NotContain("DOS 09h");
    }

    [Fact]
    public void ALongWriteBufferEndsWithEllipsis() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0x0300,
            Enumerable.Repeat((byte)'x', 64).Concat("tail"u8.ToArray()).ToArray());
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000), memory,
            (0x0000, [0xB8, 0x00, 0x20]),   // mov ax, 0x2000
            (0x0003, [0x50]),             // push ax
            (0x0004, [0x1F]),             // pop ds
            (0x0005, [0xB9, 0x46, 0x00]), // mov cx, 70
            (0x0008, [0xBA, 0x00, 0x03]), // mov dx, 0x0300
            (0x000B, [0xB4, 0x40]),       // mov ah, 0x40
            (0x000D, [0xCD, 0x21]),       // int 0x21
            (0x000F, [0xC3]));            // ret

        // Assert
        program.SourceText.Should().Contain($"DOS 40h write \"{new string('x', 64)}...\"");
    }

    [Fact]
    public void NoSnapshotsMeansNoComment() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0x0100, "Hi$"u8.ToArray());
        GeneratedCSharpProgram program = Generate(
            GeneratorInputs.Empty.ChangedMemory, memory, PrintCall(0x0100));
        GeneratedCSharpProgram noMemory = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000), GeneratorInputs.Empty.MemorySnapshot,
            PrintCall(0x0100));

        // Assert
        program.SourceText.Should().NotContain("DOS 09h");
        noMemory.SourceText.Should().NotContain("DOS 09h");
    }

    /// <summary>The <c>data_strings</c> fixture's golden keeps the <c>09h</c> comment on its
    /// generic <c>InterruptCall</c> (real dispatch: printing can terminate the program), retains
    /// the <c>40h</c> write comment next to its named call, and calls the open service by name with
    /// the snapshot's literal as the checked expected value. The message patched at run time before
    /// its print keeps the generic call and no comment: the bytes the snapshot saw are not the
    /// bytes the call reads.</summary>
    [Fact]
    public void DataStringsGoldenShowsTheWriteCommentAndTheLiterals() {
        // Arrange / Act
        string golden = File.ReadAllText(Path.Join(AppContext.BaseDirectory,
            "Resources", "cpuTests", "res", "GeneratedCode", "data_strings.ivt.spec.cs.txt"));

        // Assert
        golden.Should().Contain("// DOS 09h print \"HELLO$\"");
        golden.Should().Contain("InterruptCall(cs1, 0x0111, 0x21)");
        golden.Should().Contain("Dos.WriteFile(");
        golden.Should().Contain("// DOS 40h write handle 1 \"");
        golden.Should().Contain("Dos.OpenFile(0, Dos.StringDecoder.GetDosString(DS, 0x016F, '\\0', \"NOFILE.TXT\"))");
        golden.Should().NotContain("// DOS 3Dh");
    }

    private static readonly IReadOnlyDictionary<int, InterruptServiceEntry> PrintContracts =
        new Dictionary<int, InterruptServiceEntry> {
            [0x09] = DosEntry(0x09, "PrintString",
                CpuValues.Ah | CpuValues.Ds | CpuValues.Dx
                | CpuValues.Ss | CpuValues.Sp,
                CpuValues.None) with { StringArgument = ServiceStringArgument.DollarTerminated }
        };

    /// <summary>A 09h service without an implementation binding keeps the generic call; the
    /// string shows in the <c>// DOS</c> comment.</summary>
    [Fact]
    public void AnUnboundPrintServiceKeepsTheComment() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0x0100, "Hi$"u8.ToArray());
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000), memory, PrintContracts, PrintCall(0x0100));

        // Assert
        program.SourceText.Should().NotContain("PrintString(");
        program.SourceText.Should().Contain("InterruptCall(");
        program.SourceText.Should().Contain("// DOS 09h print \"Hi$\"");
        AssertCompiles(program);
    }

    /// <summary>When a byte of the string changed during discovery the comment is suppressed
    /// too.</summary>
    [Fact]
    public void AChangedByteDuringDiscoveryKeepsTheGenericCall() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0x0100, "Hi$"u8.ToArray());
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000, (ImageStart + 0x0101, ImageStart + 0x0102)),
            memory, PrintContracts, PrintCall(0x0100));

        // Assert
        program.SourceText.Should().Contain("InterruptCall(");
        program.SourceText.Should().NotContain("DOS 09h");
        AssertCompiles(program);
    }

    /// <summary>A byte outside the printable range is escaped in the comment.</summary>
    [Fact]
    public void AnUnprintableByteKeepsTheComment() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0x0100, [0x01, (byte)'A', (byte)'$']);
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000), memory, PrintContracts, PrintCall(0x0100));

        // Assert
        program.SourceText.Should().Contain("// DOS 09h print \"\\x01A$\"");
        AssertCompiles(program);
    }

    /// <summary>When a filename byte or its terminator changed during discovery the bound
    /// <c>OpenFile</c> call keeps its dynamic string read but drops the recorded literal: changed
    /// bytes can never be checked against a stale expectation.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void AChangedFilenameByteKeepsTheDynamicStringRead(int changedByte) {
        // Arrange: the real bound 3Dh entry of the DOS handler.
        InterruptServiceEntry openFile = InterruptServiceAttributes
            .Of(typeof(DosInt21Handler), ServiceSelector.Ah).Services
            .Select(pair => pair.Entry)
            .Single(entry => entry.SelectorValue == 0x3D);
        IReadOnlyDictionary<int, InterruptServiceEntry> contracts =
            new Dictionary<int, InterruptServiceEntry> { [0x3D] = openFile };
        byte[] memory = MemoryWith(ImageStart + 0x0200, "NAME.TXT\0"u8.ToArray());
        // The recorded IVT entry must still point at the DOS stub for the direct call.
        int ivtEntry = 0x21 * 4;
        memory[ivtEntry] = (byte)DosStub.Offset;
        memory[ivtEntry + 1] = (byte)(DosStub.Offset >> 8);
        memory[ivtEntry + 2] = (byte)DosStub.Segment;
        memory[ivtEntry + 3] = (byte)(DosStub.Segment >> 8);
        uint changed = ImageStart + 0x0200 + (uint)changedByte;

        // Act
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000, (changed, changed + 1)),
            memory, contracts,
            (0x0000, [0xB8, 0x00, 0x20]),   // mov ax, 0x2000
            (0x0003, [0x50]),             // push ax
            (0x0004, [0x1F]),             // pop ds
            (0x0005, [0xBA, 0x00, 0x02]), // mov dx, 0x0200
            (0x0008, [0xB0, 0x00]),       // mov al, 0
            (0x000A, [0xB4, 0x3D]),       // mov ah, 0x3D
            (0x000C, [0xCD, 0x21]),       // int 0x21
            (0x000E, [0xC3]));            // ret

        // Assert
        program.SourceText.Should().Contain("Dos.OpenFile(0, Dos.StringDecoder.GetDosString(DS, 0x0200, '\\0'))");
        program.SourceText.Should().NotContain("\"NAME.TXT\")");
        AssertCompiles(program);
    }

    /// <summary>A string whose terminator sits past offset <c>0xFFFF</c> still resolves: the
    /// comment only reads the snapshot bytes.</summary>
    [Fact]
    public void AnOffsetWrapKeepsTheComment() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0xFFFF, [(byte)'A', (byte)'$']);
        GeneratedCSharpProgram program = Generate(
            new ChangedMemorySnapshot {
                WatchedAreas = [(ImageStart, ImageStart + 0x1000),
                    (ImageStart + 0xFF00, ImageStart + 0x10100)],
                ChangedRanges = []
            },
            memory, PrintContracts, PrintCall(0xFFFF));

        // Assert
        program.SourceText.Should().Contain("DOS 09h print \"A$\"");
        AssertCompiles(program);
    }

    /// <summary>An untyped site cannot get the literal: the comment keeps carrying the
    /// string.</summary>
    [Fact]
    public void AnUntypedCallKeepsTheComment() {
        // Arrange / Act
        byte[] memory = MemoryWith(ImageStart + 0x0100, "Hi$"u8.ToArray());
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000), memory, PrintCall(0x0100));

        // Assert
        program.SourceText.Should().Contain("InterruptCall(biosSegment, 0x000C, 0x21)");
        program.SourceText.Should().Contain("// DOS 09h print \"Hi$\"");
        program.SourceText.Should().NotContain("\"Hi\")");
        AssertCompiles(program);
    }

    /// <summary>A typed <c>40h</c> write has no literal overload (CX is a length, not a
    /// terminator): the comment stays.</summary>
    [Fact]
    public void AHandleWriteKeepsTheComment() {
        // Arrange / Act
        IReadOnlyDictionary<int, InterruptServiceEntry> contracts = new Dictionary<int, InterruptServiceEntry> {
            [0x40] = DosEntry(0x40, "WriteToFileOrDevice",
                CpuValues.Ah | CpuValues.Ds | CpuValues.Bx
                | CpuValues.Cx | CpuValues.Dx
                | CpuValues.Ss | CpuValues.Sp,
                CpuValues.Ax | CpuValues.Carry)
        };
        byte[] memory = MemoryWith(ImageStart + 0x0100, "DATA"u8.ToArray());
        (ushort, byte[])[] call = [
            (0x0000, [0xB8, 0x00, 0x20]),
            (0x0003, [0x50]),
            (0x0004, [0x1F]),
            (0x0005, [0xBA, 0x00, 0x01]),       // mov dx, 0x0100
            (0x0008, [0xB9, 0x04, 0x00]),       // mov cx, 4
            (0x000B, [0xBB, 0x01, 0x00]),       // mov bx, 1
            (0x000E, [0xB4, 0x40]),             // mov ah, 0x40
            (0x0010, [0xCD, 0x21]),             // int 0x21
            (0x0012, [0xC3])                    // ret
        ];
        GeneratedCSharpProgram program = Generate(
            Unchanged(ImageStart, ImageStart + 0x1000), memory, contracts, call);

        // Assert
        program.SourceText.Should().Contain("InterruptCall(");
        program.SourceText.Should().Contain("// DOS 40h write handle 1 \"DATA\"");
        AssertCompiles(program);
    }

}
