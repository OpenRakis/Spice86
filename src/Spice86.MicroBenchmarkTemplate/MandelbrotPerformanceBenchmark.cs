namespace Spice86.MicroBenchmarkTemplate;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

/// <summary>
/// Standalone benchmark for running mandelbrot_bench.com and capturing performance data.
/// Can be run as: dotnet run --project src/Spice86.MicroBenchmarkTemplate -- --mandelbrot-benchmark
/// </summary>
public static class MandelbrotPerformanceBenchmark {
    private const string BenchmarkFile = "mandelbrot_bench.com";
    private const string ProfileFile = "Mandelbrot Performance Profile.json";
    
    public static void RunBenchmark(string[] args) {
        Console.WriteLine("=== Mandelbrot Performance Benchmark ===");
        Console.WriteLine();
        
        // Find the benchmark file
        string benchmarkPath = FindBenchmarkFile();
        if (benchmarkPath == null) {
            Console.WriteLine($"ERROR: Could not find {BenchmarkFile}");
            Console.WriteLine("Please ensure the benchmark file exists in Resources directory.");
            return;
        }
        
        Console.WriteLine($"Found benchmark: {benchmarkPath}");
        Console.WriteLine("This benchmark will:");
        Console.WriteLine("  - Run mandelbrot_bench.com in Spice86");
        Console.WriteLine("  - Capture FPS data via I/O port 0x99");
        Console.WriteLine("  - Run for 30 seconds");
        Console.WriteLine("  - Generate performance profile");
        Console.WriteLine();
        
        // Note: Actual implementation would require Spice86 core library integration
        Console.WriteLine("To run this benchmark:");
        Console.WriteLine($"  1. ./Spice86 -e {benchmarkPath}");
        Console.WriteLine("  2. Let it run for 30 seconds (it will auto-exit)");
        Console.WriteLine("  3. Check console output for performance data");
        Console.WriteLine();
        Console.WriteLine("For automated testing, the benchmark outputs:");
        Console.WriteLine("  - 0xFF: Test start marker");
        Console.WriteLine("  - FPS values (16-bit, 2 bytes each)");
        Console.WriteLine("  - 0xFE: Test end marker");
        Console.WriteLine("  - Total frames (16-bit)");
        Console.WriteLine("  - Average FPS (8-bit)");
        Console.WriteLine();
        Console.WriteLine("All data is sent to I/O port 0x99 for capture by test harness.");
    }
    
    private static string? FindBenchmarkFile() {
        // Try several possible locations
        string[] searchPaths = {
            Path.Combine("src", "Spice86.MicroBenchmarkTemplate", "Resources", BenchmarkFile),
            Path.Combine("Resources", BenchmarkFile),
            Path.Combine("..", "..", "Resources", BenchmarkFile),
            BenchmarkFile
        };
        
        foreach (string path in searchPaths) {
            if (File.Exists(path)) {
                return Path.GetFullPath(path);
            }
        }
        
        return null;
    }
    
    public class PerformanceProfile {
        public DateTime Timestamp { get; set; }
        public int AverageFps { get; set; }
        public int TotalFrames { get; set; }
        public List<int> FpsReadings { get; set; } = new();
        public string GitCommit { get; set; } = "unknown";
        public string Branch { get; set; } = "unknown";
    }
}
