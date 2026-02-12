namespace Spice86.Tests.Emulator.Devices;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Integration tests for Sound Blaster and OPL timing that run machine code
/// through the full emulation stack. These verify that hardware delays match
/// DOSBox Staging's behavior.
/// </summary>
[Trait("Category", "Sound")]
public class SoundIntegrationTests {
    private const int ResultPort = 0x999;
    private const int DetailsPort = 0x998;

    /// <summary>
    /// Tests that the SB DSP reset produces 0xAA after a non-zero delay.
    /// <para>
    /// The DSP reset protocol: write 1 to port 0x226 (reset on),
    /// then write 0 (reset off). DOSBox Staging schedules a 20-microsecond
    /// event before placing 0xAA in the read buffer. At 3000 cycles/ms
    /// that is 60 cycles, so the poll loop must spin several iterations
    /// before data appears — exactly like real SB hardware.
    /// </para>
    /// <para>
    /// The .asm source is in Resources/Sound/sb_reset_delay.asm,
    /// assembled with: nasm -f bin -o sb_reset_delay.com sb_reset_delay.asm
    /// DOSBox Staging at 3000 cycles/ms reports: OK after 6 iterations.
    /// </para>
    /// </summary>
    [Fact]
    public void SoundBlasterDspReset_ProducesAA_AfterMeasurableDelay() {
        string comPath = Path.Combine("Resources", "Sound", "sb_reset_delay.com");
        byte[] program = File.ReadAllBytes(comPath);

        SoundTestHandler testHandler = RunSoundTest(program, enablePit: true, maxCycles: 500000L,
            sbType: SbType.SBPro2);

        // The DSP must have responded with 0xAA (success = 0x00)
        testHandler.Results.Should().Contain(0x00, "DSP reset should succeed with 0xAA");
        testHandler.Results.Should().NotContain(0xFF, "DSP reset should not time out");

        // The delay must be non-zero: at least a few poll iterations
        testHandler.Details.Should().HaveCountGreaterThanOrEqualTo(2, "should report low and high byte of iteration count");
        int iterationCount = testHandler.Details[0] | (testHandler.Details[1] << 8);
        iterationCount.Should().BeGreaterThan(0, "reset delay should require multiple poll iterations (20us hardware delay)");
    }

    /// <summary>
    /// Runs a sound test program through the full emulation stack.
    /// </summary>
    private SoundTestHandler RunSoundTest(byte[] program, bool enablePit,
        long maxCycles, SbType sbType = SbType.None, OplMode oplMode = OplMode.None,
        [CallerMemberName] string unitTestName = "test") {
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enablePit: enablePit,
            maxCycles: maxCycles,
            installInterruptVectors: true,
            enableA20Gate: true,
            sbType: sbType,
            oplMode: oplMode
        ).Create();

        SoundTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    private class SoundTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();
        public List<byte> Details { get; } = new();

        public SoundTestHandler(State state, ILoggerService loggerService,
            IOPortDispatcher ioPortDispatcher) : base(state, true, loggerService) {
            ioPortDispatcher.AddIOPortHandler(ResultPort, this);
            ioPortDispatcher.AddIOPortHandler(DetailsPort, this);
        }

        public override void WriteByte(ushort port, byte value) {
            if (port == ResultPort) {
                Results.Add(value);
            } else if (port == DetailsPort) {
                Details.Add(value);
            }
        }
    }
}




