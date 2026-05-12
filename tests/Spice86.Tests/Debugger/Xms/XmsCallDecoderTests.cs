namespace Spice86.Tests.Debugger.Xms;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Xms;
using Spice86.Shared.Utils;

using Xunit;

public class XmsCallDecoderTests {
    private readonly XmsCallDecoder _decoder = new XmsCallDecoder();
    private readonly Memory _memory;
    private readonly State _state;

    public XmsCallDecoderTests() {
        _memory = new Memory(new AddressReadWriteBreakpoints(), new Ram(0x100000), new A20Gate());
        _state = new State(CpuModel.INTEL_80386);
    }

    [Fact]
    public void Decode_GetVersion_HasNoParameters() {
        _state.AH = 0x00;

        DecodedCall call = _decoder.Decode(_state, _memory);

        call.Subsystem.Should().Be("XMS Driver");
        call.FunctionName.Should().Contain("Get XMS Version Number");
        call.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void Decode_RequestHmaWithFFFF_NotesTsrRequest() {
        _state.AH = 0x01;
        _state.DX = 0xFFFF;

        DecodedCall call = _decoder.Decode(_state, _memory);

        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].Source.Should().Be("DX");
        call.Parameters[0].FormattedValue.Should().Be("0xFFFF");
        call.Parameters[0].Notes.Should().Contain("entire HMA");
    }

    [Fact]
    public void Decode_AllocateEmb_DecodesDxAsKbytes() {
        _state.AH = 0x09;
        _state.DX = 1024;

        DecodedCall call = _decoder.Decode(_state, _memory);

        call.FunctionName.Should().Contain("Allocate Extended Memory Block");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].Name.Should().Be("kbytes");
        call.Parameters[0].FormattedValue.Should().Be("1024");
    }

    [Fact]
    public void Decode_AllocateAnyEmb_DecodesEdxAsKbytes() {
        _state.AH = 0x89;
        _state.EDX = 0x00100000;

        DecodedCall call = _decoder.Decode(_state, _memory);

        call.FunctionName.Should().Contain("Allocate Any Extended Memory");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].Source.Should().Be("EDX");
    }

    [Theory]
    [InlineData((byte)0x0A)]
    [InlineData((byte)0x0C)]
    [InlineData((byte)0x0D)]
    [InlineData((byte)0x0E)]
    [InlineData((byte)0x8E)]
    public void Decode_HandleOnlyFunctions_DecodeDxAsHandle(byte ah) {
        _state.AH = ah;
        _state.DX = 0x0042;

        DecodedCall call = _decoder.Decode(_state, _memory);

        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].Name.Should().Be("handle");
        call.Parameters[0].Source.Should().Be("DX");
        call.Parameters[0].FormattedValue.Should().Be("66 (0x0042)");
    }

    [Fact]
    public void Decode_ReallocateEmb_DecodesBxAndHandle() {
        _state.AH = 0x0F;
        _state.BX = 2048;
        _state.DX = 0x0001;

        DecodedCall call = _decoder.Decode(_state, _memory);

        call.Parameters.Should().HaveCount(2);
        call.Parameters[0].Name.Should().Be("new kbytes");
        call.Parameters[0].FormattedValue.Should().Be("2048");
        call.Parameters[1].Name.Should().Be("handle");
    }

    [Fact]
    public void Decode_MoveBlock_DecodesMoveStructure_HandleToHandle() {
        _state.AH = 0x0B;
        _state.DS = 0x1000;
        _state.SI = 0x0000;
        uint baseAddress = MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI);
        _memory.UInt32[baseAddress + 0x0u] = 0x100u;
        _memory.UInt16[baseAddress + 0x4u] = 0x0001;
        _memory.UInt32[baseAddress + 0x6u] = 0x200u;
        _memory.UInt16[baseAddress + 0xAu] = 0x0002;
        _memory.UInt32[baseAddress + 0xCu] = 0x400u;

        DecodedCall call = _decoder.Decode(_state, _memory);

        call.FunctionName.Should().Contain("Move Extended Memory Block");
        call.Parameters.Should().HaveCount(6);
        call.Parameters[0].Source.Should().Be("DS:SI");
        call.Parameters[1].Name.Should().Be("length");
        call.Parameters[2].Name.Should().Be("source handle");
        call.Parameters[2].FormattedValue.Should().Be("1 (0x0001)");
        call.Parameters[3].Name.Should().Be("source offset");
        call.Parameters[3].Notes.Should().Contain("32-bit offset");
        call.Parameters[4].Name.Should().Be("dest handle");
        call.Parameters[5].Name.Should().Be("dest offset");
    }

    [Fact]
    public void Decode_MoveBlock_HandleZeroDecodesAsRealModePointer() {
        _state.AH = 0x0B;
        _state.DS = 0x2000;
        _state.SI = 0x0010;
        uint baseAddress = MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI);
        _memory.UInt32[baseAddress + 0x0u] = 0x10u;
        _memory.UInt16[baseAddress + 0x4u] = 0x0000;
        _memory.UInt32[baseAddress + 0x6u] = 0x12345678u;
        _memory.UInt16[baseAddress + 0xAu] = 0x0001;
        _memory.UInt32[baseAddress + 0xCu] = 0x0u;

        DecodedCall call = _decoder.Decode(_state, _memory);

        call.Parameters[2].Notes.Should().Contain("real-mode");
        call.Parameters[3].FormattedValue.Should().Be("1234:5678");
        call.Parameters[3].Notes.Should().Be("real-mode pointer");
    }

    [Fact]
    public void Decode_UnknownAh_StillReturnsCall() {
        _state.AH = 0x77;

        DecodedCall call = _decoder.Decode(_state, _memory);

        call.FunctionName.Should().Contain("AH=77h");
        call.FunctionName.Should().Contain("unknown");
        call.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void Decode_ReallocateAnyEmb_DecodesEbx() {
        _state.AH = 0x8F;
        _state.EBX = 0x40000;
        _state.DX = 0x0003;

        DecodedCall call = _decoder.Decode(_state, _memory);

        call.Parameters.Should().HaveCount(2);
        call.Parameters[0].Source.Should().Be("EBX");
        call.Parameters[1].Name.Should().Be("handle");
    }

    [Fact]
    public void Tables_ContainAllKnownXmsFunctions() {
        // Sanity: ensure the table covers the same set as the emulator's XmsSubFunctionsCodes enum.
        XmsDecodingTables.ByAh.Should().ContainKeys(
            (byte)0x00, (byte)0x01, (byte)0x02, (byte)0x03, (byte)0x04,
            (byte)0x05, (byte)0x06, (byte)0x07, (byte)0x08, (byte)0x09,
            (byte)0x0A, (byte)0x0B, (byte)0x0C, (byte)0x0D, (byte)0x0E,
            (byte)0x0F, (byte)0x10, (byte)0x11, (byte)0x12,
            (byte)0x88, (byte)0x89, (byte)0x8E, (byte)0x8F);
    }

    [Fact]
    public void Tables_ContainStandardErrorCodes() {
        XmsDecodingTables.ErrorCodes[0x80].Should().Contain("not implemented");
        XmsDecodingTables.ErrorCodes[0xA2].Should().Be("Invalid handle");
        XmsDecodingTables.ErrorCodes[0xB1].Should().Contain("UMB");
    }
}
