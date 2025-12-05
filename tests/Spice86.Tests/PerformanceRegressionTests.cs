namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;
using System.Text.Json;

using Xunit;

/// <summary>
/// Performance regression tests that verify emulator performance doesn't degrade between releases.
/// These tests run CPU-intensive workloads and compare execution metrics against baseline profiles.
/// </summary>
public class PerformanceRegressionTests {
    private const int PerformancePort = 0x99;  // Port for performance data output
    private const string BaselineDirectory = "Resources/PerformanceBaselines";

    /// <summary>
    /// Tests emulator performance using a Mandelbrot fractal calculation benchmark.
    /// This CPU-intensive workload exercises integer arithmetic, loops, and memory writes.
    /// The test runs for a fixed duration and compares FPS against baseline.
    /// Uses the mandelbrot_bench.com binary located in Resources/PerformanceTests/.
    /// </summary>
    [Fact]
    public void MandelbrotBenchmark_ShouldNotRegress() {
        // Load and run the precompiled Mandelbrot benchmark from PerformanceTests directory
        PerformanceTestHandler testHandler = RunPerformanceTest(Path.Combine("Resources", "PerformanceTests", "mandelbrot_bench.com"));
        
        // Extract performance data
        PerformanceProfile profile = ExtractPerformanceProfile(testHandler.PerformanceData);
        
        // Load or create baseline
        PerformanceProfile baseline = LoadOrCreateBaseline("Mandelbrot", profile);
        
        // Compare against baseline (allow 8% degradation tolerance)
        double tolerance = 0.08;
        profile.AverageFps.Should().BeGreaterThanOrEqualTo(
            baseline.AverageFps * (1.0 - tolerance),
            $"Performance should not degrade by more than {tolerance * 100}%");
        
        // Log comparison for visibility
        double percentChange = ((profile.AverageFps - baseline.AverageFps) / baseline.AverageFps) * 100;
        Console.WriteLine($"Mandelbrot Benchmark Results:");
        Console.WriteLine($"  Baseline: {baseline.AverageFps:F2} FPS");
        Console.WriteLine($"  Current:  {profile.AverageFps:F2} FPS");
        Console.WriteLine($"  Change:   {percentChange:+0.00;-0.00}%");
    }

    /// <summary>
    /// Runs the performance test program and returns a handler with performance data.
    /// </summary>
    private PerformanceTestHandler RunPerformanceTest(string programPath) {
        // Ensure the program path is absolute for the emulator
        string absoluteProgramPath = Path.GetFullPath(programPath);

        // Setup emulator (disable CfgCpu for now as it has issues with the program)
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: absoluteProgramPath,
            enableCfgCpu: false,  // Use traditional CPU for stability
            enablePit: true,
            recordData: false,
            maxCycles: 100000000L,  // Allow enough cycles for 30-second benchmark
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: false,
            enableEms: false
        ).Create();

        PerformanceTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    /// <summary>
    /// Extracts performance profile from raw performance data.
    /// </summary>
    private PerformanceProfile ExtractPerformanceProfile(List<byte> data) {
        if (data.Count < 3) {
            return new PerformanceProfile { TotalFrames = 0, AverageFps = 0 };
        }

        // Find end marker (0xFE) and extract total frames
        int endMarkerIndex = data.FindIndex(b => b == 0xFE);
        if (endMarkerIndex == -1 || endMarkerIndex + 2 >= data.Count) {
            return new PerformanceProfile { TotalFrames = 0, AverageFps = 0 };
        }

        // Extract total frames (16-bit value after end marker)
        ushort totalFrames = (ushort)(data[endMarkerIndex + 1] | (data[endMarkerIndex + 2] << 8));
        
        // Calculate average FPS: total frames divided by test duration
        const double TestDurationSeconds = 30.0;
        double averageFps = totalFrames / TestDurationSeconds;

        return new PerformanceProfile {
            TotalFrames = totalFrames,
            AverageFps = averageFps
        };
    }

    /// <summary>
    /// Loads baseline performance profile or creates one if it doesn't exist.
    /// </summary>
    private PerformanceProfile LoadOrCreateBaseline(string testName, PerformanceProfile currentProfile) {
        Directory.CreateDirectory(BaselineDirectory);
        string baselinePath = Path.Combine(BaselineDirectory, $"{testName}.json");

        if (File.Exists(baselinePath)) {
            string json = File.ReadAllText(baselinePath);
            PerformanceProfile? baseline = JsonSerializer.Deserialize<PerformanceProfile>(json);
            if (baseline != null) {
                return baseline;
            }
        }

        // Create new baseline
        SaveBaseline(testName, currentProfile);
        return currentProfile;
    }

    /// <summary>
    /// Saves performance profile as baseline.
    /// </summary>
    private void SaveBaseline(string testName, PerformanceProfile profile) {
        Directory.CreateDirectory(BaselineDirectory);
        string baselinePath = Path.Combine(BaselineDirectory, $"{testName}.json");
        
        JsonSerializerOptions options = new() { WriteIndented = true };
        string json = JsonSerializer.Serialize(profile, options);
        File.WriteAllText(baselinePath, json);
        
        Console.WriteLine($"Created performance baseline: {baselinePath}");
    }

    /// <summary>
    /// Captures performance data from designated I/O port.
    /// </summary>
    private class PerformanceTestHandler : DefaultIOPortHandler {
        public List<byte> PerformanceData { get; } = new();

        public PerformanceTestHandler(State state, ILoggerService loggerService,
            IOPortDispatcher ioPortDispatcher) : base(state, true, loggerService) {
            ioPortDispatcher.AddIOPortHandler(PerformancePort, this);
        }

        public override void WriteByte(ushort port, byte value) {
            if (port == PerformancePort) {
                PerformanceData.Add(value);
            }
        }
    }

    /// <summary>
    /// Represents a performance profile for a benchmark.
    /// </summary>
    private class PerformanceProfile {
        public int TotalFrames { get; set; }
        public double AverageFps { get; set; }
    }
}
