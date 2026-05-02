namespace Spice86.Tests.Debugger.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Dos;
using Spice86.Shared.Utils;

using Xunit;

public class DosInt21DecoderTests {
    private readonly DosInt21Decoder _decoder = new DosInt21Decoder();
    private readonly Memory _memory;
    private readonly State _state;

    public DosInt21DecoderTests() {
        _memory = new Memory(new AddressReadWriteBreakpoints(), new Ram(0x100000), new A20Gate());
        _state = new State(CpuModel.INTEL_80386);
    }

    [Fact]
    public void CanDecode_ReturnsTrueOnlyForVector21() {
        _decoder.CanDecode(0x21).Should().BeTrue();
        _decoder.CanDecode(0x20).Should().BeFalse();
        _decoder.CanDecode(0x2F).Should().BeFalse();
    }

    [Fact]
    public void Decode_PrintString_DecodesDsDxDollarTerminatedString() {
        WriteString(0x0040, 0x0100, "Hello, world!$");
        _state.AH = 0x09;
        _state.DS = 0x0040;
        _state.DX = 0x0100;

        DecodedCall call = _decoder.Decode(0x21, _state, _memory);

        call.Subsystem.Should().Be("DOS INT 21h");
        call.FunctionName.Should().StartWith("AH=09h ");
        call.Parameters.Should().HaveCount(1);
        DecodedParameter parameter = call.Parameters[0];
        parameter.Source.Should().Be("DS:DX");
        parameter.Kind.Should().Be(DecodedParameterKind.Memory);
        parameter.FormattedValue.Should().Contain("\"Hello, world!\"");
        parameter.Notes.Should().Contain("$");
    }

    [Fact]
    public void Decode_OpenFile_DecodesAsciiZFilenameAndAccessMode() {
        WriteString(0x1234, 0x5678, "C:\\GAME\\FILE.DAT\0");
        _state.AH = 0x3D;
        _state.DS = 0x1234;
        _state.DX = 0x5678;
        _state.AL = 0x02; // read/write

        DecodedCall call = _decoder.Decode(0x21, _state, _memory);

        call.FunctionName.Should().Contain("Open File");
        call.Parameters.Should().HaveCount(2);
        DecodedParameter filename = call.Parameters[0];
        filename.Name.Should().Be("filename");
        filename.FormattedValue.Should().Contain("C:\\GAME\\FILE.DAT");
        DecodedParameter access = call.Parameters[1];
        access.Source.Should().Be("AL");
        access.FormattedValue.Should().Contain("read/write");
    }

    [Fact]
    public void Decode_OpenFile_DecodesReadOnlyAccess() {
        _state.AH = 0x3D;
        _state.DS = 0;
        _state.DX = 0;
        _state.AL = 0x00;

        DecodedCall call = _decoder.Decode(0x21, _state, _memory);

        call.Parameters[1].FormattedValue.Should().Contain("read-only");
    }

    [Fact]
    public void Decode_QuitWithExitCode_DecodesAlAsExitCode() {
        _state.AH = 0x4C;
        _state.AL = 0x2A;

        DecodedCall call = _decoder.Decode(0x21, _state, _memory);

        call.FunctionName.Should().Contain("Terminate with Exit Code");
        call.Parameters.Should().HaveCount(1);
        DecodedParameter exitCode = call.Parameters[0];
        exitCode.Name.Should().Be("exit code");
        exitCode.RawValue.Should().Be(0x2A);
        exitCode.FormattedValue.Should().Contain("42").And.Contain("0x2A");
    }

    [Fact]
    public void Decode_DisplayOutput_DecodesPrintableAscii() {
        _state.AH = 0x02;
        _state.DL = (byte)'A';

        DecodedCall call = _decoder.Decode(0x21, _state, _memory);

        DecodedParameter ch = call.Parameters[0];
        ch.FormattedValue.Should().Contain("'A'").And.Contain("0x41");
    }

    [Fact]
    public void Decode_DisplayOutput_DecodesNonPrintableAsHexOnly() {
        _state.AH = 0x02;
        _state.DL = 0x07; // BEL

        DecodedCall call = _decoder.Decode(0x21, _state, _memory);

        DecodedParameter ch = call.Parameters[0];
        ch.FormattedValue.Should().Be("0x07");
    }

    [Fact]
    public void Decode_SelectDefaultDrive_DecodesDriveLetter() {
        _state.AH = 0x0E;
        _state.DL = 2; // C:

        DecodedCall call = _decoder.Decode(0x21, _state, _memory);

        call.Parameters[0].FormattedValue.Should().Contain("C:");
    }

    [Fact]
    public void Decode_SetInterruptVector_DecodesVectorAndHandlerAddress() {
        _state.AH = 0x25;
        _state.AL = 0x21;
        _state.DS = 0xF000;
        _state.DX = 0xFF53;

        DecodedCall call = _decoder.Decode(0x21, _state, _memory);

        call.Parameters.Should().HaveCount(2);
        call.Parameters[0].FormattedValue.Should().Be("INT 21h");
        call.Parameters[1].FormattedValue.Should().Be("F000:FF53");
    }

    [Fact]
    public void Decode_ReadFromFileOrDevice_DecodesHandleByteCountAndBuffer() {
        _state.AH = 0x3F;
        _state.BX = 1;       // STDOUT
        _state.CX = 0x0080;
        _state.DS = 0x2000;
        _state.DX = 0x0100;

        DecodedCall call = _decoder.Decode(0x21, _state, _memory);

        call.Parameters.Should().HaveCount(3);
        call.Parameters[0].FormattedValue.Should().Be("1 (STDOUT)");
        call.Parameters[1].FormattedValue.Should().Contain("128");
        call.Parameters[2].FormattedValue.Should().Be("2000:0100");
    }

    [Fact]
    public void Decode_CreateFile_DecodesAttributesBitfield() {
        _state.AH = 0x3C;
        _state.DS = 0x0;
        _state.DX = 0x0;
        _state.CX = 0x0007; // READ_ONLY | HIDDEN | SYSTEM

        DecodedCall call = _decoder.Decode(0x21, _state, _memory);

        call.Parameters[1].FormattedValue.Should().Contain("READ_ONLY")
            .And.Contain("HIDDEN")
            .And.Contain("SYSTEM");
    }

    [Fact]
    public void Decode_MoveFilePointer_DecodesSeekModeAndCxDxOffset() {
        _state.AH = 0x42;
        _state.BX = 5;
        _state.AL = 1; // from current
        _state.CX = 0x0001;
        _state.DX = 0x2000;

        DecodedCall call = _decoder.Decode(0x21, _state, _memory);

        call.Parameters.Should().HaveCount(3);
        call.Parameters[1].FormattedValue.Should().Contain("from current");
        call.Parameters[2].Source.Should().Be("CX:DX");
        call.Parameters[2].RawValue.Should().Be(0x00012000);
    }

    [Fact]
    public void Decode_UnknownAh_ReturnsGenericFunctionEntry() {
        _state.AH = 0xFE;

        DecodedCall call = _decoder.Decode(0x21, _state, _memory);

        call.FunctionName.Should().Contain("FEh");
        call.ShortDescription.Should().Contain("Unknown");
        call.Parameters.Should().BeEmpty();
    }

    private void WriteString(ushort segment, ushort offset, string value) {
        uint baseAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        for (int i = 0; i < value.Length; i++) {
            _memory.UInt8[baseAddress + (uint)i] = (byte)value[i];
        }
    }
}
