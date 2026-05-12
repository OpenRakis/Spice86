namespace Spice86.Tests.Debugger.Bios;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Bios;
using Spice86.DebuggerKnowledgeBase.Decoding;

using Xunit;

public class BiosMouseInt74DecoderTests {
    private readonly BiosMouseInt74Decoder _decoder = new BiosMouseInt74Decoder();
    private readonly IMemory _memory = Substitute.For<IMemory>();
    private readonly State _state = new State(CpuModel.INTEL_80386);

    [Fact]
    public void CanDecode_OnlyClaims74() {
        _decoder.CanDecode(0x74).Should().BeTrue();
        _decoder.CanDecode(0x33).Should().BeFalse();
        _decoder.CanDecode(0x15).Should().BeFalse();
    }

    [Fact]
    public void Decode_ReturnsBiosLevelDescription() {
        DecodedCall call = _decoder.Decode(0x74, _state, _memory);

        call.Subsystem.Should().Be("BIOS INT 74h");
        call.FunctionName.Should().Be("Mouse Hardware IRQ (PS/2)");
        call.ShortDescription.Should().Contain("BIOS-level");
        call.Parameters.Should().BeEmpty();
    }
}
