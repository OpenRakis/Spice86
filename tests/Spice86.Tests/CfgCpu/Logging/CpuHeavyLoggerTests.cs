namespace Spice86.Tests.CfgCpu.Logging;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.Logging;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Generic;

using Xunit;

public class CpuHeavyLoggerTests : IDisposable {
    private readonly EmulatorStateSerializationFolder _emulatorStateSerializationFolder = new(Path.GetTempPath());
    private readonly List<string> _filesToCleanup = new();
    private readonly State _state = new(CpuModel.INTEL_8086);
    private readonly Memory _memory = new(new AddressReadWriteBreakpoints(), new Ram(0x100000), new A20Gate());
    private readonly TestInstructionHelper _instructionHelper = new();

    private CpuHeavyLogger Create(string? logPath, AsmRenderingStyle style,
        IReadOnlyList<CompiledLogExpression>? logExpressions = null) {
        AsmRenderingConfig config = AsmRenderingConfig.Create(style);
        NodeToString nodeToString = new(config);
        return new(_emulatorStateSerializationFolder, logPath, nodeToString, _state, config,
            logExpressions ?? Array.Empty<CompiledLogExpression>());
    }

    public void Dispose() {
        foreach (string file in _filesToCleanup.Where(File.Exists)) {
            File.Delete(file);
        }
    }

    private string GetUniqueLogPath() {
        string path = Path.Join(Path.GetTempPath(), $"test_cpu_log_{Guid.NewGuid()}.log");
        _filesToCleanup.Add(path);
        return path;
    }

    [Fact]
    public void Constructor_WithDefaultPath_CreatesLogFileInDumpDirectory() {
        // Arrange
        string expectedLogPath = Path.Join(_emulatorStateSerializationFolder.Folder, "cpu_heavy.log");
        _filesToCleanup.Add(expectedLogPath);

        // Act
        using CpuHeavyLogger logger = Create(null, AsmRenderingStyle.Spice86);

        // Assert
        File.Exists(expectedLogPath).Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithCustomPath_CreatesLogFileAtCustomLocation() {
        // Arrange
        string customLogPath = GetUniqueLogPath();

        // Act
        using CpuHeavyLogger logger = Create(customLogPath, AsmRenderingStyle.Spice86);

        // Assert
        File.Exists(customLogPath).Should().BeTrue();
    }

    [Fact]
    public void LogInstruction_SingleInstruction_WritesInstruction() {
        // Arrange
        string logPath = GetUniqueLogPath();
        
        // Set predictable register values for consistent test output
        _state.EAX = 0x00000000;
        _state.EBX = 0x00000000;
        _state.ECX = 0x000000FF;
        _state.EDX = 0x000001DD;
        _state.ESI = 0x00000100;
        _state.EDI = 0x0000FFFE;
        _state.EBP = 0x0000091C;
        _state.ESP = 0x0000FFFE;
        _state.DS = 0x01DD;
        _state.ES = 0x01DD;
        _state.SS = 0x01DD;
        _state.CarryFlag = false;
        _state.ZeroFlag = false;
        _state.SignFlag = false;
        _state.OverflowFlag = false;
        _state.InterruptFlag = true;
        _state.DirectionFlag = false;
        _state.TrapFlag = false;
        _state.AuxiliaryFlag = false;
        _state.ParityFlag = false;

        CfgInstruction nop = CreateNop(new SegmentedAddress(0x01DD, 0x0100));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox)) {
            logger.LogInstruction(nop);
        }

        // Assert
        string[] logContent = File.ReadAllLines(logPath);
        logContent.Should().HaveCount(1);
        logContent[0].Should().Be("01DD:0100  nop                            EAX:00000000 EBX:00000000 ECX:000000FF EDX:000001DD ESI:00000100 EDI:0000FFFE EBP:0000091C ESP:0000FFFE DS:01DD ES:01DD SS:01DD C0 Z0 S0 O0 I1");
    }

    // --- LogExpressionCompiler / CompiledLogExpression tests ---

    [Fact]
    public void LogExpression_RegisterPlusOne_AppendsCorrectValue() {
        // Arrange
        string logPath = GetUniqueLogPath();
        _state.EAX = 0x0041;
        LogExpressionCompiler compiler = new(_state, _memory);
        CompiledLogExpression expr = compiler.Compile("life=AX+1");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { expr })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().EndWith("life:00000042");
    }

    [Fact]
    public void LogExpression_RawEbx_AppendsCorrectValue() {
        // Arrange
        string logPath = GetUniqueLogPath();
        _state.EBX = 0xDEADBEEF;
        LogExpressionCompiler compiler = new(_state, _memory);
        CompiledLogExpression expr = compiler.Compile("raw=EBX");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { expr })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().EndWith("raw:DEADBEEF");
    }

    [Fact]
    public void LogExpression_SegmentRegister_AppendsCorrectValue() {
        // Arrange
        string logPath = GetUniqueLogPath();
        _state.DS = 0x01DD;
        LogExpressionCompiler compiler = new(_state, _memory);
        CompiledLogExpression expr = compiler.Compile("seg=DS");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { expr })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().EndWith("seg:000001DD");
    }

    [Fact]
    public void LogExpression_ArithmeticSum_AppendsCorrectValue() {
        // Arrange
        string logPath = GetUniqueLogPath();
        _state.EAX = 3;
        _state.EBX = 5;
        LogExpressionCompiler compiler = new(_state, _memory);
        CompiledLogExpression expr = compiler.Compile("sum=AX+BX");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { expr })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().EndWith("sum:00000008");
    }

    [Fact]
    public void LogExpression_LeftShift_AppendsCorrectValue() {
        // Arrange
        string logPath = GetUniqueLogPath();
        _state.EAX = 0x0001;
        LogExpressionCompiler compiler = new(_state, _memory);
        CompiledLogExpression expr = compiler.Compile("shifted=AX<<2");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { expr })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().EndWith("shifted:00000004");
    }

    [Fact]
    public void LogExpression_BitwiseAnd_AppendsCorrectValue() {
        // Arrange
        string logPath = GetUniqueLogPath();
        _state.EAX = 0xDEADBEEF;
        LogExpressionCompiler compiler = new(_state, _memory);
        CompiledLogExpression expr = compiler.Compile("masked=EAX&0xFF");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { expr })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().EndWith("masked:000000EF");
    }

    [Fact]
    public void LogExpression_BooleanTrue_AppendOne() {
        // Arrange
        string logPath = GetUniqueLogPath();
        _state.EAX = 0;
        LogExpressionCompiler compiler = new(_state, _memory);
        CompiledLogExpression expr = compiler.Compile("flag=AX==0");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { expr })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().EndWith("flag:00000001");
    }

    [Fact]
    public void LogExpression_BooleanFalse_AppendZero() {
        // Arrange
        string logPath = GetUniqueLogPath();
        _state.EAX = 1;
        LogExpressionCompiler compiler = new(_state, _memory);
        CompiledLogExpression expr = compiler.Compile("flag=AX==0");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { expr })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().EndWith("flag:00000000");
    }

    [Fact]
    public void LogExpression_AbsoluteByteMemory_AppendsCorrectValue() {
        // Arrange
        string logPath = GetUniqueLogPath();
        _memory.UInt8[0x1234] = 0xAB;
        LogExpressionCompiler compiler = new(_state, _memory);
        CompiledLogExpression expr = compiler.Compile("b=byte ptr [0x1234]");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { expr })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().EndWith("b:000000AB");
    }

    [Fact]
    public void LogExpression_AbsoluteWordMemory_AppendsCorrectValue() {
        // Arrange
        string logPath = GetUniqueLogPath();
        _memory.UInt16[0x1000] = 0x1234;
        LogExpressionCompiler compiler = new(_state, _memory);
        CompiledLogExpression expr = compiler.Compile("w=word ptr [0x1000]");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { expr })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().EndWith("w:00001234");
    }

    [Fact]
    public void LogExpression_AbsoluteDwordMemory_AppendsCorrectValue() {
        // Arrange
        string logPath = GetUniqueLogPath();
        _memory.UInt32[0x2000] = 0x12345678;
        LogExpressionCompiler compiler = new(_state, _memory);
        CompiledLogExpression expr = compiler.Compile("d=dword ptr [0x2000]");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { expr })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().EndWith("d:12345678");
    }

    [Fact]
    public void LogExpression_SegmentedByteMemory_AppendsCorrectValue() {
        // Arrange
        string logPath = GetUniqueLogPath();
        // segment 0x01DD, offset 0x0100 => linear 0x01DD0 + 0x0100 = 0x1ED0
        uint linear = (uint)(0x01DD * 16 + 0x0100);
        _memory.UInt8[linear] = 0xFF;
        LogExpressionCompiler compiler = new(_state, _memory);
        // Parser numeric-segment syntax: "segment:[offset]" (e.g. 0x01DD:[0x0100])
        CompiledLogExpression expr = compiler.Compile("sb=byte ptr 0x01DD:[0x0100]");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { expr })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().EndWith("sb:000000FF");
    }

    [Fact]
    public void LogExpression_SegmentedWordWithDsRegister_AppendsCorrectValue() {
        // Arrange
        string logPath = GetUniqueLogPath();
        _state.DS = 0x01DD;
        uint linear = (uint)(0x01DD * 16 + 0x0200);
        _memory.UInt16[linear] = 0xBEEF;
        LogExpressionCompiler compiler = new(_state, _memory);
        // Parser syntax for segmented pointers with register segment: "ds:[offset]"
        CompiledLogExpression expr = compiler.Compile("sw=word ptr ds:[0x0200]");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { expr })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().EndWith("sw:0000BEEF");
    }

    [Fact]
    public void LogExpression_MultipleExpressions_AllAppearInOrder() {
        // Arrange
        string logPath = GetUniqueLogPath();
        _state.EAX = 0x0001;
        _state.EBX = 0x0002;
        LogExpressionCompiler compiler = new(_state, _memory);
        CompiledLogExpression exprA = compiler.Compile("a=AX");
        CompiledLogExpression exprB = compiler.Compile("b=BX");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { exprA, exprB })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().Contain("a:00000001");
        line.Should().Contain("b:00000002");
        line.IndexOf("a:", StringComparison.Ordinal).Should().BeLessThan(line.IndexOf("b:", StringComparison.Ordinal));
    }

    [Fact]
    public void LogExpression_EmptyList_BehavesIdenticalToNoExpressions() {
        // Arrange
        string logPathNoExpr = GetUniqueLogPath();
        string logPathEmpty = GetUniqueLogPath();
        _state.EAX = 0xAABBCCDD;
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPathNoExpr, AsmRenderingStyle.DosBox)) {
            logger.LogInstruction(nop);
        }
        using (CpuHeavyLogger logger = Create(logPathEmpty, AsmRenderingStyle.DosBox,
            Array.Empty<CompiledLogExpression>())) {
            logger.LogInstruction(nop);
        }
        // Assert
        string lineNoExpr = File.ReadAllLines(logPathNoExpr)[0];
        string lineEmpty = File.ReadAllLines(logPathEmpty)[0];
        lineEmpty.Should().Be(lineNoExpr);
    }

    [Fact]
    public void LogExpressionCompiler_MissingEquals_ThrowsArgumentException() {
        // Arrange
        LogExpressionCompiler compiler = new(_state, _memory);
        // Act
        Action act = () => compiler.Compile("missingequals");
        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LogExpressionCompiler_EmptyName_ThrowsArgumentException() {
        // Arrange
        LogExpressionCompiler compiler = new(_state, _memory);
        // Act
        Action act = () => compiler.Compile("=AX");
        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LogExpressionCompiler_ExpressionContainsEquals_SplitsOnFirstEquals() {
        // Arrange
        string logPath = GetUniqueLogPath();
        _state.EAX = 0;
        LogExpressionCompiler compiler = new(_state, _memory);
        // "flag=AX==0" => name "flag", expression "AX==0"
        CompiledLogExpression expr = compiler.Compile("flag=AX==0");
        CfgInstruction nop = CreateNop(new SegmentedAddress(0, 0));
        // Act
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox, new[] { expr })) {
            logger.LogInstruction(nop);
        }
        // Assert
        string line = File.ReadAllLines(logPath)[0];
        line.Should().EndWith("flag:00000001");
    }

    private CfgInstruction CreateNop(SegmentedAddress address) {
        return _instructionHelper.WriteAndParse(address, w => w.WriteNop());
    }
}
