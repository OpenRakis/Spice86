namespace Spice86.Tests.AsmFixtures;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Audio.Backend;
using Spice86.Audio.Common;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Tests.Utility;

/// <summary>
/// Scenarios of the BIOS, RTC, video and sound integration programs under Resources/RtcTests,
/// Resources/VgaTests and Resources/Sound. Every scenario copies its program into a fresh per-run
/// working directory, so the discovery run and the generated execution start from identical host
/// files, and each run installs its own test-port capture handler on its own machine. The oracle keeps
/// the original completion markers, BCD/date result-port checks, VGA character and attribute cells
/// (including columns 40 and 79) and the timing-sensitive sound iteration counts in both modes.
/// </summary>
internal static class BiosDeviceScenarioCatalog {
    private const string RtcSubDirectory = "RtcTests";
    private const string VgaSubDirectory = "VgaTests";
    private const string SoundSubDirectory = "Sound";
    private const string TestPortResultsObservation = "testPortResults";
    private const string GusDeviceObservation = "gusDevice";
    private const string GusPlaybackRecorderObservation = "gusPlaybackRecorder";
    private const byte TestPortSuccess = 0x00;
    private const byte TestPortFailure = 0xFF;
    private const long RtcMaxCycles = 100000L;
    private const long RtcWaitMaxCycles = 500000L;
    private const long VgaMaxCycles = 300000L;
    private const long SoundMaxCycles = 500000L;
    private const long GusPlaybackMaxCycles = 100000L;

    /// <summary>Every BIOS, video and sound scenario, so one entry point can cover them all.</summary>
    public static IReadOnlyList<AsmScenario> Scenarios => [.. RtcScenarios, .. VgaScenarios, .. SoundScenarios];

    /// <summary>RTC/CMOS programs: direct CMOS port access, BIOS INT 1A time services, DOS INT 21h date
    /// and time services, the INT 15h AH=83h event wait and the INT 70h RTC periodic interrupt setup.</summary>
    public static IReadOnlyList<AsmScenario> RtcScenarios => [
        RtcProgram("cmos_ports.com", 0x07, RtcMaxCycles),
        RtcProgram("bios_int1a.com", 0x06, RtcMaxCycles),
        RtcProgram("dos_int21h.com", 0x0B, RtcMaxCycles),
        RtcProgram("bios_int15h_83h.com", 0x05, RtcMaxCycles),
        RtcProgram("bios_int70_wait.com", 0x07, RtcWaitMaxCycles)
    ];

    /// <summary>VGA BIOS programs: the scroll-up that must clear every column of the vacated row,
    /// and the graphics-mode scroll-up that must actually move the rows it scrolls.</summary>
    public static IReadOnlyList<AsmScenario> VgaScenarios => [VgaScrollClearsFullRow(), VgaGraphicsScrollMovesRows()];

    /// <summary>Sound programs: the two OPL timer/write-delay programs and the Sound Blaster DSP reset
    /// delay, each run with the sound model the original test exposed to the machine.</summary>
    public static IReadOnlyList<AsmScenario> SoundScenarios =>
        [.. OplScenarios, SoundBlasterResetDelay(), GusPlaybackSamples()];

    /// <summary>The OPL timer/write-delay programs, run with OPL3 synthesis.</summary>
    public static IReadOnlyList<AsmScenario> OplScenarios => [OplWriteDelay(), OplReadDelay()];

    /// <summary>The Sound Blaster DSP reset-delay program, run with an SBPro2 model.</summary>
    public static IReadOnlyList<AsmScenario> SoundBlasterScenarios => [SoundBlasterResetDelay()];

    /// <summary>Finds a BIOS, video or sound scenario by its stable identity.</summary>
    /// <param name="identity">Identity of the scenario.</param>
    /// <returns>The scenario with that identity.</returns>
    /// <exception cref="InvalidOperationException">No BIOS or device scenario has that identity.</exception>
    public static AsmScenario Scenario(string identity) {
        return Scenarios.FirstOrDefault(scenario => scenario.Identity == identity)
            ?? throw new InvalidOperationException($"No BIOS or device scenario with identity '{identity}'.");
    }

    /// <summary>The VGA scroll-up program: it fills rows 0-2 through INT 10h AH=06h/09h, scrolls the
    /// window up one line and must leave every column of the vacated row cleared with blank attribute
    /// 0x07, while the copied rows keep attribute 0x1F.</summary>
    /// <returns>The scenario for scroll_up_clears_full_row.com.</returns>
    public static AsmScenario VgaScrollClearsFullRow() {
        const string fileName = "scroll_up_clears_full_row.com";
        const string identity = "scroll_up_clears_full_row";
        return new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            string programPath = CopyResourceFile(VgaSubDirectory, fileName, directory);
            AsmRunSettings settings = new() {
                BinName = programPath,
                ScenarioName = identity,
                EnablePit = true,
                MaxCycles = VgaMaxCycles,
                InstallInterruptVectors = true,
                CDrive = directory
            };
            return new AsmFixture(settings, [], AssertScrollClearedEveryColumn);
        }, [$"{VgaSubDirectory}/{fileName}"]);
    }

    /// <summary>The VGA graphics scroll-up program: it fills character rows 0-2 in mode 4 through
    /// INT 10h AH=09h, scrolls the window up one line and must move the rows rather than blank
    /// them. The graphics move path takes its copy width from the window extent and its height
    /// from the window row count, so a full-width scroll whose destination is the window origin
    /// must still copy the full width.</summary>
    /// <returns>The scenario for graphics_scroll_moves_rows.com.</returns>
    public static AsmScenario VgaGraphicsScrollMovesRows() {
        const string fileName = "graphics_scroll_moves_rows.com";
        const string identity = "graphics_scroll_moves_rows";
        return new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            string programPath = CopyResourceFile(VgaSubDirectory, fileName, directory);
            AsmRunSettings settings = new() {
                BinName = programPath,
                ScenarioName = identity,
                EnablePit = true,
                MaxCycles = VgaMaxCycles,
                InstallInterruptVectors = true,
                CDrive = directory,
                ConfigureMachine = machine => RegisterTestPortHandler(scope, machine)
            };
            return new AsmFixture(settings, [], AssertGraphicsScrollMovedRows);
        }, [$"{VgaSubDirectory}/{fileName}"]);
    }

    private static void AssertGraphicsScrollMovedRows(AsmFixtureResult result) {
        TestIoPortHandler testHandler =
            result.Observations.Get<TestIoPortHandler>(TestPortResultsObservation);
        testHandler.Results.Should().Contain(TestPortSuccess,
            "scrolling up in graphics mode must copy the rows it scrolls");
        testHandler.Results.Should().NotContain(TestPortFailure,
            "a zero-width move blanks the rows instead of shifting them");
    }

    /// <summary>The OPL write-delay program: the classic Adlib detection sequence must overflow Timer 1    /// before the first poll iteration completes, so the iteration count the program reports is exactly 1.</summary>
    /// <returns>The scenario for opl_write_delay.com.</returns>
    public static AsmScenario OplWriteDelay() {
        const string fileName = "opl_write_delay.com";
        const string identity = "opl_write_delay";
        return SoundProgram(identity, fileName, SbType.None, OplMode.Opl3, testHandler => {
            AssertOplTimerFired(testHandler, fileName);
            int iterationCount = ReportedWord(testHandler);
            iterationCount.Should().Be(1,
                "OPL Timer 1 should overflow before the first poll iteration completes");
        });
    }

    /// <summary>The OPL read-delay program: it counts the status reads between Timer 1 start and
    /// overflow, which must be at least one read on real hardware.</summary>
    /// <returns>The scenario for opl_read_delay.com.</summary>
    public static AsmScenario OplReadDelay() {
        const string fileName = "opl_read_delay.com";
        const string identity = "opl_read_delay";
        return SoundProgram(identity, fileName, SbType.None, OplMode.Opl3, testHandler => {
            AssertOplTimerFired(testHandler, fileName);
            int readCount = ReportedWord(testHandler);
            readCount.Should().BeGreaterThanOrEqualTo(1,
                "Real hardware reports 2 status reads before Timer 1 overflow");
        });
    }

    /// <summary>The Sound Blaster DSP reset-delay program: the reset must answer 0xAA after the 20us
    /// hardware delay, which requires at least one poll iteration.</summary>
    /// <returns>The scenario for sb_reset_delay.com.</summary>
    public static AsmScenario SoundBlasterResetDelay() {
        const string fileName = "sb_reset_delay.com";
        const string identity = "sb_reset_delay";
        return SoundProgram(identity, fileName, SbType.SBPro2, OplMode.None, testHandler => {
            testHandler.Results.Should().Contain(TestPortSuccess, "DSP reset should succeed with 0xAA");
            testHandler.Results.Should().NotContain(TestPortFailure, "DSP reset should not time out");
            testHandler.Details.Should().HaveCountGreaterThanOrEqualTo(2,
                "should report low and high byte of iteration count");
            ReportedWord(testHandler).Should().BeGreaterThan(0,
                "reset delay should require multiple poll iterations (20us hardware delay)");
        });
    }

    /// <summary>Programs nonzero 8-bit and 16-bit sample loops through GUS ports and checks their panned output.</summary>
    /// <returns>The scenario for gus_playback_samples.com.</returns>
    public static AsmScenario GusPlaybackSamples() {
        const string fileName = "gus_playback_samples.com";
        const string identity = "gus_playback_samples";
        return new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            string programPath = CopyResourceFile(SoundSubDirectory, fileName, directory);
            AsmRunSettings settings = new() {
                BinName = programPath,
                ScenarioName = identity,
                EnablePit = true,
                EnableGus = true,
                MaxCycles = GusPlaybackMaxCycles,
                InstallInterruptVectors = true,
                EnableA20Gate = true,
                ConfigureMachine = machine => {
                    RegisterTestPortHandler(scope, machine);
                    GravisUltraSound gus = machine.GravisUltraSound
                        ?? throw new InvalidOperationException("GUS was explicitly enabled for this scenario.");
                    scope.Register(GusDeviceObservation, gus);
                    GusPlaybackRecorder recorder = new(gus, machine.EmulationLoopScheduler);
                    scope.Register(GusPlaybackRecorderObservation, recorder);
                    recorder.Start();
                }
            };
            return new AsmFixture(settings, [], AssertGusPlaybackSamples);
        }, [$"{SoundSubDirectory}/{fileName}", $"{SoundSubDirectory}/gus_playback_samples.s"]);
    }

    private static void AssertGusPlaybackSamples(AsmFixtureResult result) {
        TestIoPortHandler testHandler =
            result.Observations.Get<TestIoPortHandler>(TestPortResultsObservation);
        testHandler.Results.Should().Contain(0xA5);
        testHandler.Results.Should().Contain(0x5A);
        GravisUltraSound gus = result.Observations.Get<GravisUltraSound>(GusDeviceObservation);
        GusPlaybackRecorder recorder =
            result.Observations.Get<GusPlaybackRecorder>(GusPlaybackRecorderObservation);
        gus.BasePort.Should().Be(0x240);
        recorder.SelectedRegister.Should().Be(0x8D);
        recorder.DramAddress.Should().Be(0x2007);
        recorder.ResetRegister.Should().Be(0x03);
        recorder.ActiveVoices.Should().BeGreaterThan(0);
        recorder.Voice0Generated8BitMs.Should().BeGreaterThan(0);
        recorder.Voice1Generated16BitMs.Should().BeGreaterThan(0);
        AudioFrame[] capturedFrames = recorder.Frames.ToArray();
        capturedFrames.Should().Contain(frame => frame.Left > 0,
            "the guest-programmed 8-bit voice must produce left-channel samples");
        capturedFrames.Should().Contain(frame => frame.Right > 0,
            "the guest-programmed 16-bit voice must produce right-channel samples");
        capturedFrames.Select(frame => frame.Left).Distinct().Should().HaveCountGreaterThan(1,
            "the looping volume envelope must change the left-channel sample values");
        capturedFrames.Select(frame => frame.Right).Distinct().Should().HaveCountGreaterThan(1,
            "the looping volume envelope must change the right-channel sample values");
    }

    private sealed class GusPlaybackRecorder {
        private readonly GravisUltraSound _gus;
        private readonly RWQueue<AudioFrame> _outputQueue;
        private readonly Spice86.Core.Emulator.VM.DeviceScheduler.DeviceScheduler _scheduler;
        private readonly AudioFrame[] _buffer = new AudioFrame[4096];
        private readonly Spice86.Core.Emulator.VM.DeviceScheduler.EventHandler _captureHandler;

        public List<AudioFrame> Frames { get; } = new();
        public byte SelectedRegister { get; private set; }
        public int DramAddress { get; private set; }
        public byte ResetRegister { get; private set; }
        public int ActiveVoices { get; private set; }
        public uint Voice0Generated8BitMs { get; private set; }
        public uint Voice1Generated16BitMs { get; private set; }

        public GusPlaybackRecorder(GravisUltraSound gus,
            Spice86.Core.Emulator.VM.DeviceScheduler.DeviceScheduler scheduler) {
            _gus = gus;
            _outputQueue = gus.OutputQueue;
            _scheduler = scheduler;
            _captureHandler = Capture;
        }

        public void Start() {
            _scheduler.AddEvent(_captureHandler, 1, 0);
        }

        private void Capture(uint _) {
            SelectedRegister = _gus.SelectedRegister;
            DramAddress = _gus.DramAddress;
            ResetRegister = _gus.ResetRegister;
            ActiveVoices = Math.Max(ActiveVoices, _gus.ActiveVoices);
            Voice0Generated8BitMs = Math.Max(Voice0Generated8BitMs, _gus.Voices[0].Generated8BitMs);
            Voice1Generated16BitMs = Math.Max(Voice1Generated16BitMs, _gus.Voices[1].Generated16BitMs);

            int frameCount = _outputQueue.BulkDequeue(_buffer, _buffer.Length);
            for (int i = 0; i < frameCount; i++) {
                Frames.Add(_buffer[i]);
            }
            _scheduler.AddEvent(_captureHandler, 1, 0);
        }
    }

    /// <summary>Runs an RTC program that reports every checked call through the shared test result ports
    /// and whose last test writes its own test number to the details port.</summary>
    /// <param name="fileName">File name of the program under Resources/RtcTests.</param>
    /// <param name="completionTestNumber">Test number of the last test, which only runs when every
    /// earlier test, including the invalid BCD and invalid date checks, succeeded.</param>
    /// <param name="maxCycles">Cycle failsafe of the run.</param>
    /// <returns>The scenario for that program.</returns>
    private static AsmScenario RtcProgram(string fileName, byte completionTestNumber, long maxCycles) {
        string identity = Path.GetFileNameWithoutExtension(fileName);
        return new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            string programPath = CopyResourceFile(RtcSubDirectory, fileName, directory);
            AsmRunSettings settings = new() {
                BinName = programPath,
                ScenarioName = identity,
                EnablePit = true,
                MaxCycles = maxCycles,
                InstallInterruptVectors = true,
                EnableA20Gate = false,
                EnableXms = false,
                EnableEms = false,
                CDrive = directory,
                ConfigureMachine = machine => RegisterTestPortHandler(scope, machine)
            };
            return new AsmFixture(settings, [], result => {
                TestIoPortHandler testHandler =
                    result.Observations.Get<TestIoPortHandler>(TestPortResultsObservation);
                testHandler.Results.Should().Contain(TestPortSuccess, $"{fileName} must report success");
                testHandler.Results.Should().NotContain(TestPortFailure, $"{fileName} must not report failure");
                testHandler.Details.Should().Contain(completionTestNumber,
                    $"all {completionTestNumber} tests of {fileName} should have completed");
            });
        }, [$"{RtcSubDirectory}/{fileName}"]);
    }

    private static AsmScenario SoundProgram(string identity, string fileName, SbType sbType, OplMode oplMode,
        Action<TestIoPortHandler> assertTiming) {
        return new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            string programPath = CopyResourceFile(SoundSubDirectory, fileName, directory);
            AsmRunSettings settings = new() {
                BinName = programPath,
                ScenarioName = identity,
                EnablePit = true,
                MaxCycles = SoundMaxCycles,
                InstallInterruptVectors = true,
                EnableA20Gate = true,
                SbType = sbType,
                OplMode = oplMode,
                ConfigureMachine = machine => RegisterTestPortHandler(scope, machine)
            };
            return new AsmFixture(settings, [], result => {
                TestIoPortHandler testHandler =
                    result.Observations.Get<TestIoPortHandler>(TestPortResultsObservation);
                assertTiming(testHandler);
            });
        }, [$"{SoundSubDirectory}/{fileName}"]);
    }

    private static void AssertOplTimerFired(TestIoPortHandler testHandler, string fileName) {
        testHandler.Results.Should().Contain(TestPortSuccess, "OPL Timer 1 should overflow after 80us");
        testHandler.Results.Should().NotContain(0x01, "OPL status should be clear after timer reset");
        testHandler.Results.Should().NotContain(0x02, $"{fileName}: OPL Timer 1 should not time out");
        testHandler.Details.Should().HaveCountGreaterThanOrEqualTo(2,
            "should report low and high byte of iteration count");
    }

    private static void AssertScrollClearedEveryColumn(AsmFixtureResult result) {
        AssertVideoCell(result.Memory, 0, 0, 'B', 0x1F, "row 0 col 0 should have 'B' scrolled up from row 1");
        AssertVideoCell(result.Memory, 0, 79, 'B', 0x1F, "row 0 col 79 (rightmost) should have 'B' scrolled up from row 1");
        AssertVideoCell(result.Memory, 1, 0, 'C', 0x1F, "row 1 col 0 should have 'C' scrolled up from row 2");
        AssertVideoCell(result.Memory, 1, 79, 'C', 0x1F, "row 1 col 79 (rightmost) should have 'C' scrolled up from row 2");
        AssertVideoCell(result.Memory, 2, 0, ' ', 0x07, "row 2 col 0 should be cleared to space+0x07");
        AssertVideoCell(result.Memory, 2, 40, ' ', 0x07, "row 2 col 40 (middle) should be cleared to space+0x07");
        AssertVideoCell(result.Memory, 2, 79, ' ', 0x07, "row 2 col 79 (rightmost) should be cleared to space+0x07");
    }

    private static void AssertVideoCell(IMemory memory, int row, int col, char character, byte attribute,
        string because) {
        VideoMemoryReader.ReadVideoCell(memory, row, col).Should().Be((character, attribute), because);
    }

    private static int ReportedWord(TestIoPortHandler testHandler) {
        return testHandler.Details[0] | (testHandler.Details[1] << 8);
    }

    private static void RegisterTestPortHandler(AsmRunScope scope, Machine machine) {
        scope.Register(TestPortResultsObservation,
            new TestIoPortHandler(machine.CpuState, Substitute.For<ILogger>(), machine.IoPortDispatcher));
    }

    private static string CopyResourceFile(string resourceSubDirectory, string fileName, string targetDirectory) {
        string source = Path.Join(AppContext.BaseDirectory, "Resources", resourceSubDirectory, fileName);
        string target = Path.Join(targetDirectory, fileName);
        File.Copy(source, target, overwrite: true);
        return target;
    }
}
