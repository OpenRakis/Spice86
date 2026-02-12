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
/// Integration tests for OPL timer and IO port latency that run machine code
/// through the full emulation stack. These verify that OPL hardware delays
/// match DOSBox Staging's behavior at 3000 cycles/ms.
/// </summary>
[Trait("Category", "Sound")]
public class OplIntegrationTests {
    private const int ResultPort = 0x999;
    private const int DetailsPort = 0x998;

    /// <summary>
    /// Tests that OPL register writes with standard inter-write delays
    /// result in correct Timer 1 operation (classic Adlib detection sequence).
    /// <para>
    /// The test performs the full Adlib detection protocol:
    /// reset timers, verify clean status, set Timer 1 = 0xFF (80us period),
    /// start Timer 1, poll status until overflow. Standard OPL2 write delays
    /// are used: 6 IO reads address-to-data (~3.3us), 35 IO reads post-data
    /// (~23us).
    /// </para>
    /// <para>
    /// DOSBox Staging at 3000 cycles/ms reports: OK after 1 iteration.
    /// The long post-data delay (35 IO reads) means most of the 80us
    /// has elapsed before the first poll.
    /// </para>
    /// <para>
    /// The .asm source is in Resources/Sound/opl_write_delay.asm,
    /// assembled with: nasm -f bin -o opl_write_delay.com opl_write_delay.asm
    /// </para>
    /// </summary>
    [Fact]
    public void OplWriteDelay_Timer1Fires_AfterCorrectDelay() {
        string comPath = Path.Combine("Resources", "Sound", "opl_write_delay.com");
        byte[] program = File.ReadAllBytes(comPath);

        SoundTestHandler testHandler = RunSoundTest(program, enablePit: true, maxCycles: 500000L,
            oplMode: OplMode.Opl3);

        // Timer 1 must have fired (success = 0x00)
        testHandler.Results.Should().Contain(0x00, "OPL Timer 1 should overflow after 80us");
        testHandler.Results.Should().NotContain(0x01, "OPL status should be clear after timer reset");
        testHandler.Results.Should().NotContain(0x02, "OPL Timer 1 should not time out");

        // DOSBox Staging at 3000 cycles/ms: 1 iteration
        testHandler.Details.Should().HaveCountGreaterThanOrEqualTo(2,
            "should report low and high byte of iteration count");
        int iterationCount = testHandler.Details[0] | (testHandler.Details[1] << 8);
        iterationCount.Should().Be(1,
            "DOSBox Staging at 3000 cycles/ms reports 1 poll iteration " +
            "(35 post-data IO reads consume most of the 80us timer period)");
    }

    /// <summary>
    /// Tests OPL port read latency by counting how many status register
    /// reads (port 0x388) occur between Timer 1 start and overflow detection.
    /// <para>
    /// Each IO read consumes delay cycles, so the iteration count directly
    /// reflects how IO read latency is modeled. Timer 1 with counter 0xFF
    /// fires after (256-255)*80us = 80us. The test uses a minimal post-start
    /// delay (1 IO read) so counting begins as early as possible.
    /// </para>
    /// <para>
    /// DOSBox Staging at 3000 cycles/ms reports: OK after 2 reads.
    /// </para>
    /// <para>
    /// The .asm source is in Resources/Sound/opl_read_delay.asm,
    /// assembled with: nasm -f bin -o opl_read_delay.com opl_read_delay.asm
    /// </para>
    /// </summary>
    [Fact]
    public void OplReadDelay_StatusReadCount_MatchesDosBox() {
        string comPath = Path.Combine("Resources", "Sound", "opl_read_delay.com");
        byte[] program = File.ReadAllBytes(comPath);

        SoundTestHandler testHandler = RunSoundTest(program, enablePit: true, maxCycles: 500000L,
            oplMode: OplMode.Opl3);

        // Timer 1 must have fired (success = 0x00)
        testHandler.Results.Should().Contain(0x00, "OPL Timer 1 should overflow after 80us");
        testHandler.Results.Should().NotContain(0x01, "OPL status should be clear after timer reset");
        testHandler.Results.Should().NotContain(0x02, "OPL Timer 1 should not time out");

        // DOSBox Staging at 3000 cycles/ms: 2 reads.
        testHandler.Details.Should().HaveCountGreaterThanOrEqualTo(2,
            "should report low and high byte of read count");
        int readCount = testHandler.Details[0] | (testHandler.Details[1] << 8);
        readCount.Should().Be(2,
            "DOSBox Staging at 3000 cycles/ms reports 2 status reads before Timer 1 overflow");
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




