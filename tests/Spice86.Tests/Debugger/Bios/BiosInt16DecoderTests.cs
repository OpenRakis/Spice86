namespace Spice86.Tests.Debugger.Bios;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Bios;
using Spice86.DebuggerKnowledgeBase.Decoding;

using Xunit;

public class BiosInt16DecoderTests {
    private readonly BiosInt16Decoder _decoder = new BiosInt16Decoder();
    private readonly IMemory _memory = Substitute.For<IMemory>();
    private readonly State _state = new State(CpuModel.INTEL_80386);

    [Fact]
    public void Decode_GetKeystroke_HasNoParameters() {
        _state.AH = 0x00;

        DecodedCall call = _decoder.Decode(0x16, _state, _memory);

        call.FunctionName.Should().Contain("Get Keystroke");
        call.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void Decode_PushKeystroke_SplitsCxIntoScanAndAscii() {
        _state.AH = 0x05;
        _state.CX = 0x1E41; // CH = scan A, CL = ASCII 'A'

        DecodedCall call = _decoder.Decode(0x16, _state, _memory);

        call.FunctionName.Should().Contain("Push Keystroke");
        call.Parameters.Should().HaveCount(2);
        call.Parameters[0].FormattedValue.Should().Be("0x1E");
        call.Parameters[1].FormattedValue.Should().Contain("'A'");
    }

    [Fact]
    public void Decode_SetTypematic_DecodesDelayAndRate() {
        _state.AH = 0x03;
        _state.BH = 0x02;
        _state.BL = 0x05;

        DecodedCall call = _decoder.Decode(0x16, _state, _memory);

        call.Parameters.Should().HaveCount(2);
        call.Parameters[0].Notes.Should().Contain("750ms");
    }
}
