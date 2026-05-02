namespace Spice86.Tests.Debugger.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Dos;

using Xunit;

public class DosMissingInterruptDecoderTests {
    private readonly IMemory _memory = Substitute.For<IMemory>();
    private readonly State _state = new State(CpuModel.INTEL_80386);

    [Fact]
    public void Int22_DecoderClaimsOnlyVector22AndDescribesTerminateAddress() {
        DosInt22Decoder decoder = new DosInt22Decoder();

        decoder.CanDecode(0x22).Should().BeTrue();
        decoder.CanDecode(0x21).Should().BeFalse();
        DecodedCall call = decoder.Decode(0x22, _state, _memory);
        call.Subsystem.Should().Be("DOS INT 22h");
        call.FunctionName.Should().Contain("Terminate Address");
    }

    [Fact]
    public void Int23_DecoderClaimsOnlyVector23AndDescribesCtrlBreakHandler() {
        DosInt23Decoder decoder = new DosInt23Decoder();

        decoder.CanDecode(0x23).Should().BeTrue();
        decoder.CanDecode(0x22).Should().BeFalse();
        DecodedCall call = decoder.Decode(0x23, _state, _memory);
        call.Subsystem.Should().Be("DOS INT 23h");
        call.FunctionName.Should().Contain("Control-Break");
    }

    [Fact]
    public void Int24_DiskWriteError_DecodesDriveAndErrorCode() {
        DosInt24Decoder decoder = new DosInt24Decoder();
        // disk error (bit7 clear), write (bit0 set), data area (bits1-2=11), Fail allowed (bit5)
        _state.AH = 0x27;
        _state.AL = 0x02; // C:
        _state.DI = 0x000B; // read fault
        _state.BP = 0x1234;
        _state.SI = 0x5678;

        DecodedCall call = decoder.Decode(0x24, _state, _memory);

        call.Subsystem.Should().Be("DOS INT 24h");
        call.FunctionName.Should().Be("Critical Error Handler");
        call.Parameters.Should().Contain(p => p.Source == "AH" && p.FormattedValue.Contains("disk error"));
        call.Parameters.Should().Contain(p => p.Source == "AH" && p.FormattedValue.Contains("write"));
        call.Parameters.Should().Contain(p => p.Source == "AL" && p.FormattedValue.Contains("C:"));
        call.Parameters.Should().Contain(p => p.Source == "DI (low)" && p.FormattedValue.Contains("read fault"));
        call.Parameters.Should().Contain(p => p.Source == "BP:SI" && p.FormattedValue == "1234:5678");
    }

    [Fact]
    public void Int24_NonDiskError_OmitsDriveParameter() {
        DosInt24Decoder decoder = new DosInt24Decoder();
        _state.AH = 0x80;
        _state.AL = 0x05;

        DecodedCall call = decoder.Decode(0x24, _state, _memory);

        call.Parameters.Should().NotContain(p => p.Source == "AL");
        call.Parameters.Should().Contain(p => p.Source == "AH" && p.FormattedValue.Contains("non-disk error"));
    }

    [Fact]
    public void Int25_DecodesAbsoluteDiskRead() {
        DosInt25Decoder decoder = new DosInt25Decoder();
        _state.AL = 0x00;
        _state.CX = 4;
        _state.DX = 0x0010;
        _state.DS = 0x2000;
        _state.BX = 0x0100;

        decoder.CanDecode(0x25).Should().BeTrue();
        DecodedCall call = decoder.Decode(0x25, _state, _memory);
        call.Subsystem.Should().Be("DOS INT 25h");
        call.FunctionName.Should().Be("Absolute Disk Read");
        call.Parameters.Should().Contain(p => p.Source == "AL" && p.FormattedValue.Contains("A:"));
        call.Parameters.Should().Contain(p => p.Source == "CX" && p.FormattedValue == "4 sector(s)");
        call.Parameters.Should().Contain(p => p.Source == "DX" && p.FormattedValue == "0x0010");
        call.Parameters.Should().Contain(p => p.Source == "DS:BX" && p.FormattedValue == "2000:0100");
    }

    [Fact]
    public void Int26_DecodesAbsoluteDiskWrite() {
        DosInt26Decoder decoder = new DosInt26Decoder();
        _state.AL = 0x01;
        _state.CX = 1;
        _state.DX = 0x00FF;
        _state.DS = 0x3000;
        _state.BX = 0x0200;

        decoder.CanDecode(0x26).Should().BeTrue();
        DecodedCall call = decoder.Decode(0x26, _state, _memory);
        call.Subsystem.Should().Be("DOS INT 26h");
        call.FunctionName.Should().Be("Absolute Disk Write");
        call.Parameters.Should().Contain(p => p.Source == "AL" && p.FormattedValue.Contains("B:"));
        call.Parameters.Should().Contain(p => p.Source == "DS:BX" && p.FormattedValue == "3000:0200");
    }

    [Fact]
    public void Int28_DecoderClaimsOnly28AndDescribesIdle() {
        DosInt28Decoder decoder = new DosInt28Decoder();

        decoder.CanDecode(0x28).Should().BeTrue();
        decoder.CanDecode(0x27).Should().BeFalse();
        DecodedCall call = decoder.Decode(0x28, _state, _memory);
        call.Subsystem.Should().Be("DOS INT 28h");
        call.FunctionName.Should().Be("DOS Idle");
    }

    [Fact]
    public void Int2A_NetworkInstallationQuery_NamesFunction() {
        DosInt2ADecoder decoder = new DosInt2ADecoder();
        _state.AH = 0x00;

        decoder.CanDecode(0x2A).Should().BeTrue();
        decoder.CanDecode(0x2F).Should().BeFalse();
        DecodedCall call = decoder.Decode(0x2A, _state, _memory);
        call.Subsystem.Should().Be("DOS INT 2Ah");
        call.FunctionName.Should().Contain("Network Installation Query");
    }

    [Fact]
    public void Int2A_BeginCriticalSection_IncludesSectionId() {
        DosInt2ADecoder decoder = new DosInt2ADecoder();
        _state.AH = 0x80;
        _state.AL = 0x01;

        DecodedCall call = decoder.Decode(0x2A, _state, _memory);

        call.FunctionName.Should().Contain("Begin DOS Critical Section");
        call.Parameters.Should().Contain(p => p.Source == "AL" && p.FormattedValue == "0x01");
    }

    [Fact]
    public void Int2A_UnknownSubFunction_ReportsAh() {
        DosInt2ADecoder decoder = new DosInt2ADecoder();
        _state.AH = 0xAB;

        DecodedCall call = decoder.Decode(0x2A, _state, _memory);

        call.FunctionName.Should().Contain("AB");
        call.FunctionName.Should().Contain("unknown");
    }
}
