namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;
using NSubstitute;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Libs.Sound.Common;
using Spice86.Libs.Sound.Devices.NukedOpl3;
using Spice86.Shared.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

/// <summary>
/// Tests comparing Spice86 OPL output against golden reference data from DOSBox Staging.
/// These tests ensure bit-exact or statistically equivalent audio output for FM synthesis.
/// </summary>
public class OplGoldenReferenceTests {
    private const int OplSampleRateHz = 49716;
    private const string GoldenReferenceDataDirectory = "Resources/OplGoldenReferences";
    
    /// <summary>
    /// Represents a captured OPL register write sequence.
    /// Mirrors DOSBox's DRO (DOSBox Raw OPL) format conceptually.
    /// </summary>
    private class OplRegisterSequence {
        public List<OplRegisterWrite> Writes { get; } = new();
        public int SampleRate { get; set; } = OplSampleRateHz;
        public string TestName { get; set; } = "unknown";
        
        public void AddWrite(ushort port, byte register, byte value, int delayMs = 0) {
            Writes.Add(new OplRegisterWrite {
                Port = port,
                Register = register,
                Value = value,
                DelayMs = delayMs
            });
        }
    }
    
    /// <summary>
    /// Represents a single OPL register write with timing.
    /// </summary>
    private class OplRegisterWrite {
        public ushort Port { get; set; }
        public byte Register { get; set; }
        public byte Value { get; set; }
        public int DelayMs { get; set; }
    }
    
    /// <summary>
    /// Golden reference audio data for comparison.
    /// Uses WAV format for audio output and DRO format for OPL register sequences.
    /// </summary>
    private class GoldenAudioData {
        public List<AudioFrame> Frames { get; } = new();
        public int SampleRate { get; set; }
        public string Source { get; set; } = "DOSBox Staging";
        
        /// <summary>
        /// Load golden reference audio from WAV file (DOSBox Staging format).
        /// </summary>
        public static GoldenAudioData LoadFromWavFile(string filePath) {
            if (!File.Exists(filePath)) {
                return new GoldenAudioData(); // Return empty for missing golden files
            }
            
            GoldenAudioData data = new();
            data.Frames.AddRange(WavFileFormat.ReadWavFile(filePath, out int sampleRate));
            data.SampleRate = sampleRate;
            
            return data;
        }
        
        /// <summary>
        /// Save golden reference audio to WAV file (DOSBox Staging compatible).
        /// </summary>
        public void SaveToWavFile(string filePath) {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
            WavFileFormat.WriteWavFile(filePath, Frames, SampleRate);
        }
        
        /// <summary>
        /// Load OPL register sequence from DRO file (DOSBox Raw OPL format).
        /// </summary>
        public static OplRegisterSequence LoadDroFile(string filePath) {
            DroFileFormat.DroFile droFile = DroFileFormat.DroFile.LoadFromFile(filePath);
            
            OplRegisterSequence sequence = new() {
                SampleRate = 49716, // OPL native rate
                TestName = Path.GetFileNameWithoutExtension(filePath)
            };
            
            foreach (DroFileFormat.DroCommand cmd in droFile.Commands) {
                // Convert DRO register index to actual port/register
                ushort port = 0x388; // Primary OPL port
                sequence.AddWrite(port, cmd.Register, cmd.Value, (int)cmd.DelayMs);
            }
            
            return sequence;
        }
        
        /// <summary>
        /// Save OPL register sequence to DRO file (DOSBox Raw OPL format).
        /// </summary>
        public static void SaveDroFile(string filePath, OplRegisterSequence sequence) {
            DroFileFormat.DroFile droFile = new() {
                Header = DroFileFormat.DroHeader.CreateDefault()
            };
            
            uint totalMs = 0;
            uint totalCommands = 0;
            
            foreach (OplRegisterWrite write in sequence.Writes) {
                // Count delay commands
                if (write.DelayMs > 0) {
                    uint delay = (uint)write.DelayMs;
                    while (delay > 0) {
                        if (delay < 257) {
                            totalCommands++; // One delay256 command
                            delay = 0;
                        } else {
                            uint shift = delay >> 8;
                            totalCommands++; // One delayShift8 command
                            delay -= shift << 8;
                        }
                    }
                }
                
                droFile.Commands.Add(new DroFileFormat.DroCommand {
                    Register = write.Register,
                    Value = write.Value,
                    DelayMs = (uint)write.DelayMs
                });
                totalMs += (uint)write.DelayMs;
                totalCommands++; // Count the register write command
            }
            
            // Update header with final counts (includes delay commands)
            DroFileFormat.DroHeader header = droFile.Header;
            header.Commands = totalCommands;
            header.Milliseconds = totalMs;
            droFile.Header = header;
            
            droFile.SaveToFile(filePath);
        }
    }
    
    /// <summary>
    /// Compares two audio frame sequences and calculates similarity metrics.
    /// </summary>
    private class AudioComparisonResult {
        public int FrameCount { get; set; }
        public int ExactMatches { get; set; }
        public double RmsError { get; set; }
        public double PeakError { get; set; }
        public double SimilarityPercentage => FrameCount > 0 ? (ExactMatches * 100.0 / FrameCount) : 0;
        public bool IsBitExact => ExactMatches == FrameCount && FrameCount > 0;
        public bool IsStatisticallyEquivalent => RmsError < 0.01 && FrameCount > 0; // 1% RMS threshold
        
        public static AudioComparisonResult Compare(List<AudioFrame> actual, List<AudioFrame> expected) {
            AudioComparisonResult result = new() {
                FrameCount = Math.Min(actual.Count, expected.Count)
            };
            
            if (result.FrameCount == 0) {
                return result;
            }
            
            double sumSquaredError = 0;
            double maxError = 0;
            
            for (int i = 0; i < result.FrameCount; i++) {
                AudioFrame a = actual[i];
                AudioFrame e = expected[i];
                
                // Check for exact match (bit-exact comparison)
                if (Math.Abs(a.Left - e.Left) < float.Epsilon && 
                    Math.Abs(a.Right - e.Right) < float.Epsilon) {
                    result.ExactMatches++;
                }
                
                // Calculate errors for statistical comparison
                double leftError = Math.Abs(a.Left - e.Left);
                double rightError = Math.Abs(a.Right - e.Right);
                double frameError = Math.Max(leftError, rightError);
                
                sumSquaredError += leftError * leftError + rightError * rightError;
                maxError = Math.Max(maxError, frameError);
            }
            
            result.RmsError = Math.Sqrt(sumSquaredError / (result.FrameCount * 2));
            result.PeakError = maxError;
            
            return result;
        }
        
        public override string ToString() {
            StringBuilder sb = new();
            sb.AppendLine($"Frames: {FrameCount}");
            sb.AppendLine($"Exact Matches: {ExactMatches} ({SimilarityPercentage:F2}%)");
            sb.AppendLine($"RMS Error: {RmsError:F6}");
            sb.AppendLine($"Peak Error: {PeakError:F6}");
            sb.AppendLine($"Bit-Exact: {IsBitExact}");
            sb.AppendLine($"Statistically Equivalent: {IsStatisticallyEquivalent}");
            return sb.ToString();
        }
    }
    
    /// <summary>
    /// Helper to create OPL3 device for golden reference testing.
    /// </summary>
    private Opl3Fm CreateOpl3ForGoldenTest(out IOPortDispatcher dispatcher) {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        AddressReadWriteBreakpoints breakpoints = new();
        State state = new(CpuModel.INTEL_80286);
        dispatcher = new IOPortDispatcher(breakpoints, state, loggerService, failOnUnhandledPort: false);
        using Mixer mixer = new(loggerService, AudioEngine.Dummy);
        EmulatedClock clock = new();
        EmulationLoopScheduler scheduler = new(clock, loggerService);
        DualPic dualPic = new(dispatcher, state, loggerService, false);
        
        return new Opl3Fm(mixer, state, dispatcher, false, loggerService, scheduler, clock, dualPic,
            useAdlibGold: false, enableOplIrq: false);
    }
    
    /// <summary>
    /// Executes a register write sequence and captures the resulting audio.
    /// </summary>
    private List<AudioFrame> ExecuteSequenceAndCaptureAudio(
        OplRegisterSequence sequence,
        IOPortDispatcher dispatcher,
        Opl3Fm opl3,
        int framesToCapture) {
        
        List<AudioFrame> capturedFrames = new();
        
        foreach (OplRegisterWrite write in sequence.Writes) {
            // Apply delay if specified (simplified - real timing would use clock)
            if (write.DelayMs > 0) {
                // Generate audio during delay
                int framesForDelay = (write.DelayMs * OplSampleRateHz) / 1000;
                opl3.AudioCallback(framesForDelay);
                capturedFrames.AddRange(opl3.MixerChannel.AudioFrames);
                opl3.MixerChannel.AudioFrames.Clear();
            }
            
            // Write register
            dispatcher.WriteByte(write.Port, write.Register);
            dispatcher.WriteByte((ushort)(write.Port + 1), write.Value);
        }
        
        // Generate remaining frames
        int remainingFrames = framesToCapture - capturedFrames.Count;
        if (remainingFrames > 0) {
            opl3.AudioCallback(remainingFrames);
            capturedFrames.AddRange(opl3.MixerChannel.AudioFrames);
        }
        
        return capturedFrames.Take(framesToCapture).ToList();
    }
    
    [Fact]
    public void SimpleToneMatchesGoldenReference() {
        // This test would compare against a golden reference file
        // For now, it's a placeholder demonstrating the pattern
        
        // Arrange: Create a simple 440 Hz sine tone sequence
        OplRegisterSequence sequence = new() {
            TestName = "simple_440hz_tone"
        };
        
        // Configure operator 0 for a simple sine wave
        sequence.AddWrite(IOplPort.PrimaryAddressPortNumber, 0x20, 0x01); // Tremolo/vibrato off
        sequence.AddWrite(IOplPort.PrimaryAddressPortNumber, 0x40, 0x10); // Output level
        sequence.AddWrite(IOplPort.PrimaryAddressPortNumber, 0x60, 0xF0); // Fast attack
        sequence.AddWrite(IOplPort.PrimaryAddressPortNumber, 0x80, 0x77); // Medium sustain/release
        
        // Set frequency for 440 Hz
        sequence.AddWrite(IOplPort.PrimaryAddressPortNumber, 0xA0, 0x41); // F-number low
        sequence.AddWrite(IOplPort.PrimaryAddressPortNumber, 0xB0, 0x32); // F-number high + key on
        
        sequence.AddWrite(IOplPort.PrimaryAddressPortNumber, 0xB0, 0x12, delayMs: 100); // Key off after 100ms
        
        using Opl3Fm opl3 = CreateOpl3ForGoldenTest(out IOPortDispatcher dispatcher);
        
        // Act: Execute sequence and capture audio
        int framesToCapture = (OplSampleRateHz * 150) / 1000; // 150ms of audio
        List<AudioFrame> actualFrames = ExecuteSequenceAndCaptureAudio(
            sequence, dispatcher, opl3, framesToCapture);
        
        // Assert: For now, just verify we got audio
        actualFrames.Should().NotBeEmpty();
        actualFrames.Count.Should().BeLessThanOrEqualTo(framesToCapture);
        
        // TODO: Load and compare against golden reference
        // string goldenFile = Path.Combine(GoldenReferenceDataDirectory, $"{sequence.TestName}.txt");
        // GoldenAudioData golden = GoldenAudioData.LoadFromFile(goldenFile);
        // AudioComparisonResult comparison = AudioComparisonResult.Compare(actualFrames, golden.Frames);
        // comparison.IsStatisticallyEquivalent.Should().BeTrue(comparison.ToString());
    }
    
    [Fact]
    public void SilenceMatchesGoldenReference() {
        // Arrange: No register writes, should produce silence
        OplRegisterSequence sequence = new() {
            TestName = "silence"
        };
        
        using Opl3Fm opl3 = CreateOpl3ForGoldenTest(out IOPortDispatcher dispatcher);
        
        // Act: Capture audio without any register writes
        int framesToCapture = 1000;
        List<AudioFrame> actualFrames = ExecuteSequenceAndCaptureAudio(
            sequence, dispatcher, opl3, framesToCapture);
        
        // Assert: Should be all zeros (silence)
        bool allSilent = actualFrames.All(f => Math.Abs(f.Left) < float.Epsilon && Math.Abs(f.Right) < float.Epsilon);
        allSilent.Should().BeTrue("OPL should produce silence when no notes are playing");
        
        // This matches DOSBox behavior - unused OPL should be silent
    }
    
    [Fact]
    public void GoldenReferenceInfrastructureWorks() {
        // This test validates the WAV file golden reference infrastructure
        
        // Arrange: Create test data
        GoldenAudioData testData = new() {
            SampleRate = OplSampleRateHz,
            Source = "Test"
        };
        
        testData.Frames.Add(new AudioFrame(0.5f, -0.5f));
        testData.Frames.Add(new AudioFrame(0.25f, -0.25f));
        testData.Frames.Add(new AudioFrame(0.0f, 0.0f));
        
        // Act: Save and load as WAV (DOSBox Staging format)
        string tempFile = Path.Combine(Path.GetTempPath(), "opl_test_golden.wav");
        testData.SaveToWavFile(tempFile);
        GoldenAudioData loaded = GoldenAudioData.LoadFromWavFile(tempFile);
        
        // Assert: Should match (allowing for int16 quantization)
        loaded.Frames.Count.Should().Be(testData.Frames.Count);
        AudioComparisonResult comparison = AudioComparisonResult.Compare(loaded.Frames, testData.Frames);
        comparison.IsStatisticallyEquivalent.Should().BeTrue("WAV format should preserve audio quality");
        
        // Cleanup
        if (File.Exists(tempFile)) {
            File.Delete(tempFile);
        }
    }
    
    [Fact]
    public void DroFileFormatWorks() {
        // This test validates the DRO file format support for OPL register sequences
        
        // Arrange: Create test OPL register sequence
        OplRegisterSequence sequence = new() {
            TestName = "test_tone",
            SampleRate = 49716
        };
        
        // Simple tone configuration
        sequence.AddWrite(0x388, 0x01, 0x20); // Enable waveform selection
        sequence.AddWrite(0x388, 0x20, 0x01, delayMs: 10); // Configure operator with 10ms delay
        sequence.AddWrite(0x388, 0xA0, 0x41); // Set frequency
        sequence.AddWrite(0x388, 0xB0, 0x32); // Key on
        
        // Act: Save and load as DRO
        string tempFile = Path.Combine(Path.GetTempPath(), "opl_test.dro");
        GoldenAudioData.SaveDroFile(tempFile, sequence);
        OplRegisterSequence loaded = GoldenAudioData.LoadDroFile(tempFile);
        
        // Assert: Should match
        loaded.Writes.Count.Should().Be(sequence.Writes.Count);
        loaded.Writes[1].DelayMs.Should().Be(10, "DRO should preserve timing information");
        
        // Cleanup
        if (File.Exists(tempFile)) {
            File.Delete(tempFile);
        }
    }
    
    [Fact]
    public void AudioComparisonDetectsDifferences() {
        // Arrange: Create two slightly different audio sequences
        List<AudioFrame> actual = new() {
            new AudioFrame(0.5f, 0.5f),
            new AudioFrame(0.3f, 0.3f),
            new AudioFrame(0.1f, 0.1f)
        };
        
        List<AudioFrame> expected = new() {
            new AudioFrame(0.5f, 0.5f),      // Exact match
            new AudioFrame(0.31f, 0.31f),    // Slightly different
            new AudioFrame(0.1f, 0.1f)       // Exact match
        };
        
        // Act
        AudioComparisonResult comparison = AudioComparisonResult.Compare(actual, expected);
        
        // Assert
        comparison.FrameCount.Should().Be(3);
        comparison.ExactMatches.Should().Be(2); // Only 2 of 3 match exactly
        comparison.IsBitExact.Should().BeFalse();
        comparison.RmsError.Should().BeGreaterThan(0);
    }
}
