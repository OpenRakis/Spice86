namespace Spice86.Tests.Debugger.Ems;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Ems;

using Xunit;

public class EmsInt67DecoderTests {
    private readonly EmsInt67Decoder _decoder = new EmsInt67Decoder();
    private readonly IMemory _memory = Substitute.For<IMemory>();
    private readonly State _state = new State(CpuModel.INTEL_80386);

    [Fact]
    public void CanDecode_OnlyClaims67() {
        _decoder.CanDecode(0x67).Should().BeTrue();
        _decoder.CanDecode(0x21).Should().BeFalse();
        _decoder.CanDecode(0x10).Should().BeFalse();
    }

    [Fact]
    public void Decode_GetStatus_HasNoParameters() {
        _state.AH = 0x40;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.Subsystem.Should().Be("EMS INT 67h");
        call.FunctionName.Should().Contain("Get Manager Status");
        call.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void Decode_AllocatePages_DecodesBxAsPageCount() {
        _state.AH = 0x43;
        _state.BX = 16;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.FunctionName.Should().Contain("Allocate Pages");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].Source.Should().Be("BX");
        call.Parameters[0].FormattedValue.Should().Be("16");
    }

    [Fact]
    public void Decode_MapHandlePage_DecodesAlBxDx() {
        _state.AH = 0x44;
        _state.AL = 2;
        _state.BX = 5;
        _state.DX = 0x0001;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.Parameters.Should().HaveCount(3);
        call.Parameters[0].Name.Should().Be("physical page");
        call.Parameters[0].FormattedValue.Should().Be("2");
        call.Parameters[1].Name.Should().Be("logical page");
        call.Parameters[1].Notes.Should().BeNull();
        call.Parameters[2].Name.Should().Be("handle");
        call.Parameters[2].FormattedValue.Should().Contain("0x0001");
    }

    [Fact]
    public void Decode_UnmapHandlePage_FormatsLogicalPageFFFFAsUnmap() {
        _state.AH = 0x44;
        _state.AL = 0;
        _state.BX = 0xFFFF;
        _state.DX = 0;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.Parameters[1].FormattedValue.Should().Be("0xFFFF");
        call.Parameters[1].Notes.Should().Be("unmap");
    }

    [Fact]
    public void Decode_DeallocatePages_DecodesHandleFromDx() {
        _state.AH = 0x45;
        _state.DX = 0x0007;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.FunctionName.Should().Contain("Deallocate Pages");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].Name.Should().Be("handle");
        call.Parameters[0].RawValue.Should().Be(0x0007);
    }

    [Fact]
    public void Decode_GetEmmVersion_HasNoParameters() {
        _state.AH = 0x46;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.FunctionName.Should().Contain("Get EMM Version");
        call.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void Decode_GetAllHandlePages_DecodesEsDiBuffer() {
        _state.AH = 0x4D;
        _state.ES = 0x1234;
        _state.DI = 0x0010;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].Source.Should().Be("ES:DI");
        call.Parameters[0].FormattedValue.Should().Be("1234:0010");
    }

    [Fact]
    public void Decode_MapMultiple_PhysicalMode_DecodesAllInputs() {
        _state.AH = 0x50;
        _state.AL = 0x00;
        _state.CX = 4;
        _state.DX = 0x0002;
        _state.DS = 0x2000;
        _state.SI = 0x0100;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.Parameters.Should().HaveCount(4);
        call.Parameters[0].FormattedValue.Should().Contain("physical page numbers");
        call.Parameters[1].FormattedValue.Should().Be("4");
        call.Parameters[2].Name.Should().Be("handle");
        call.Parameters[3].Source.Should().Be("DS:SI");
        call.Parameters[3].FormattedValue.Should().Be("2000:0100");
    }

    [Fact]
    public void Decode_MapMultiple_SegmentedMode_DescribesMode() {
        _state.AH = 0x50;
        _state.AL = 0x01;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.Parameters[0].FormattedValue.Should().Contain("segmented addresses");
    }

    [Fact]
    public void Decode_ReallocatePages_DecodesBxAndDx() {
        _state.AH = 0x51;
        _state.BX = 32;
        _state.DX = 0x0003;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.Parameters.Should().HaveCount(2);
        call.Parameters[0].Name.Should().Be("new pages");
        call.Parameters[0].FormattedValue.Should().Be("32");
        call.Parameters[1].Name.Should().Be("handle");
    }

    [Fact]
    public void Decode_GetSetHandleName_DecodesGetMode() {
        _state.AH = 0x53;
        _state.AL = 0x00;
        _state.DX = 0x0004;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.Parameters.Should().HaveCount(2);
        call.Parameters[0].Name.Should().Be("operation");
        call.Parameters[0].FormattedValue.Should().Contain("get");
    }

    [Fact]
    public void Decode_GetSetHandleName_DecodesSetMode() {
        _state.AH = 0x53;
        _state.AL = 0x01;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.Parameters[0].FormattedValue.Should().Contain("set");
    }

    [Fact]
    public void Decode_HardwareInformation_DecodesArrayMode() {
        _state.AH = 0x59;
        _state.AL = 0x00;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].FormattedValue.Should().Contain("hardware configuration array");
    }

    [Fact]
    public void Decode_HardwareInformation_DecodesRawPagesMode() {
        _state.AH = 0x59;
        _state.AL = 0x01;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.Parameters[0].FormattedValue.Should().Contain("unallocated raw page counts");
    }

    [Fact]
    public void Decode_UnknownAh_ReturnsGenericFunction() {
        _state.AH = 0xCC;

        DecodedCall call = _decoder.Decode(0x67, _state, _memory);

        call.FunctionName.Should().Contain("CCh");
        call.ShortDescription.Should().Contain("Unknown");
        call.Parameters.Should().BeEmpty();
    }
}
