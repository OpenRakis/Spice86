namespace Spice86.Tests.Debugger.Gus;

using FluentAssertions;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Gus;

using Xunit;

public class GusIoPortDecoderTests {
    private readonly GusIoPortDecoder _decoder = new GusIoPortDecoder(0x240);

    [Theory]
    [InlineData((ushort)0x240)] // Mix Control
    [InlineData((ushort)0x246)] // IRQ Status
    [InlineData((ushort)0x248)] // AdLib Timer Command
    [InlineData((ushort)0x24B)] // IRQ/DMA Control Set
    [InlineData((ushort)0x24F)] // Register Controls Select
    [InlineData((ushort)0x340)] // MIDI Control
    [InlineData((ushort)0x341)] // MIDI Data
    [InlineData((ushort)0x342)] // GF1 Page
    [InlineData((ushort)0x343)] // GF1 Register Select
    [InlineData((ushort)0x344)] // GF1 Data Low
    [InlineData((ushort)0x345)] // GF1 Data High
    [InlineData((ushort)0x346)] // DRAM Address Low
    [InlineData((ushort)0x347)] // DRAM Address High
    public void CanDecode_ClaimsKnownGusOffsets(ushort port) {
        _decoder.CanDecode(port).Should().BeTrue();
    }

    [Theory]
    [InlineData((ushort)0x241)] // Reserved offset 0x001
    [InlineData((ushort)0x244)] // Reserved offset 0x004
    [InlineData((ushort)0x250)] // Different base
    [InlineData((ushort)0x388)] // OPL territory
    [InlineData((ushort)0x300)] // MPU-401 territory
    [InlineData((ushort)0x000)]
    public void CanDecode_RejectsUnknownOrOtherCardPorts(ushort port) {
        _decoder.CanDecode(port).Should().BeFalse();
    }

    [Fact]
    public void DecodeWrite_MixControl_HasGusSubsystem() {
        DecodedCall call = _decoder.DecodeWrite(0x240, 0x0B, 1);

        call.Subsystem.Should().Be("Gravis Ultrasound I/O Ports");
        call.FunctionName.Should().Contain("Mix Control");
    }

    [Fact]
    public void DecodeRead_IrqStatus_DescribesStatusBits() {
        DecodedCall call = _decoder.DecodeRead(0x246, 0x80, 1);

        call.FunctionName.Should().Contain("IRQ Status");
        call.ShortDescription.Should().Contain("DMA TC IRQ");
    }

    [Fact]
    public void DecodeWrite_RegisterControlsSelect_DecodesIrqLatchBits() {
        // 0x40 → "select IRQ-channel latch"
        DecodedCall call = _decoder.DecodeWrite(0x24F, 0x40, 1);

        call.FunctionName.Should().Contain("Register Controls Select");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].FormattedValue.Should().Contain("select IRQ-channel latch");
    }

    [Fact]
    public void DecodeWrite_RegisterControlsSelect_DecodesDmaLatchBits() {
        DecodedCall call = _decoder.DecodeWrite(0x24F, 0x80, 1);

        call.Parameters[0].FormattedValue.Should().Contain("select DMA-channel latch");
    }

    [Fact]
    public void DecodeWrite_Gf1Page_DecodesVoiceNumber() {
        DecodedCall call = _decoder.DecodeWrite(0x342, 0x05, 1);

        call.FunctionName.Should().Contain("GF1 Page");
        call.Parameters[0].FormattedValue.Should().Contain("voice 5");
    }

    [Fact]
    public void DecodeWrite_Gf1RegisterSelect_LooksUpVoiceControl() {
        DecodedCall call = _decoder.DecodeWrite(0x343, 0x00, 1);

        call.Parameters[0].FormattedValue.Should().Contain("Voice Control");
    }

    [Fact]
    public void DecodeWrite_Gf1RegisterSelect_LooksUpFrequencyControl() {
        DecodedCall call = _decoder.DecodeWrite(0x343, 0x01, 1);

        call.Parameters[0].FormattedValue.Should().Contain("Frequency Control");
    }

    [Fact]
    public void DecodeWrite_Gf1RegisterSelect_LooksUpResetRegister() {
        DecodedCall call = _decoder.DecodeWrite(0x343, 0x4C, 1);

        call.Parameters[0].FormattedValue.Should().Contain("Reset");
    }

    [Fact]
    public void DecodeWrite_Gf1RegisterSelect_ReadOnlyMirrorIsLabeled() {
        // 0x8F is the read-only IRQ source register.
        DecodedCall call = _decoder.DecodeWrite(0x343, 0x8F, 1);

        call.Parameters[0].FormattedValue.Should().Contain("IRQ Source");
        call.Parameters[0].FormattedValue.Should().Contain("read");
    }

    [Fact]
    public void DecodeWrite_Gf1DataLow_16bit_RendersAsWord() {
        DecodedCall call = _decoder.DecodeWrite(0x344, 0xABCD, 2);

        call.FunctionName.Should().Contain("GF1 Data Port Low");
        call.Parameters[0].Name.Should().Be("data16");
        call.Parameters[0].FormattedValue.Should().Be("0xABCD");
    }

    [Fact]
    public void DecodeWrite_Gf1DataLow_8bit_RendersAsByte() {
        DecodedCall call = _decoder.DecodeWrite(0x344, 0x42, 1);

        call.Parameters[0].Name.Should().Be("data_low");
        call.Parameters[0].FormattedValue.Should().Be("0x42");
    }

    [Fact]
    public void DecodeRead_MidiControl_HasUartCommandsInDescription() {
        DecodedCall call = _decoder.DecodeRead(0x340, 0x00, 1);

        call.FunctionName.Should().Contain("MIDI Control");
        call.ShortDescription.Should().Contain("MPU-401-compatible");
    }

    [Fact]
    public void DecodeWrite_DramAddressLow_IsLabeled() {
        DecodedCall call = _decoder.DecodeWrite(0x346, 0x10, 1);

        call.FunctionName.Should().Contain("DRAM I/O Address Low");
    }

    [Fact]
    public void Subsystem_IsConsistent() {
        DecodedCall a = _decoder.DecodeWrite(0x240, 0x00, 1);
        DecodedCall b = _decoder.DecodeRead(0x246, 0x00, 1);

        a.Subsystem.Should().Be("Gravis Ultrasound I/O Ports");
        b.Subsystem.Should().Be("Gravis Ultrasound I/O Ports");
    }

    [Fact]
    public void DecodeWrite_AtAlternateBase_UsesProvidedBasePort() {
        GusIoPortDecoder decoder220 = new GusIoPortDecoder(0x220);

        decoder220.CanDecode(0x220).Should().BeTrue();
        decoder220.CanDecode(0x322).Should().BeTrue(); // 0x220 + 0x102 = 0x322 → Gf1Page
        decoder220.CanDecode(0x240).Should().BeFalse();
    }
}
