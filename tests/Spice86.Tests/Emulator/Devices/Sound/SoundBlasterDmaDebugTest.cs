namespace Spice86.Tests.Emulator.Devices.Sound;

using System;
using System.IO;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Debug test to trace DMA execution with actual logging output
/// </summary>
public class SoundBlasterDmaDebugTest {
    private readonly ITestOutputHelper _output;
    
    public SoundBlasterDmaDebugTest(ITestOutputHelper output) {
        _output = output;
    }
    
    [Fact]
    public async System.Threading.Tasks.Task Debug_8Bit_Single_Cycle_DMA_With_Logging() {
        // Load the test binary
        string asmBinary = Path.Combine("Resources", "SoundBlasterTests", "sb_dma_8bit_single.bin");
        if (!File.Exists(asmBinary)) {
            _output.WriteLine($"Test binary not found: {asmBinary}");
            return;
        }
        
        byte[] program = File.ReadAllBytes(asmBinary);
        string filePath = Path.GetFullPath("debug_dma_test.com");
        File.WriteAllBytes(filePath, program);
        
        _output.WriteLine($"Running DMA test from: {filePath}");
        _output.WriteLine($"Program size: {program.Length} bytes");
        
        // Create emulator with PIT enabled for timing and verbose logs enabled
        Spice86DependencyInjection spice86 = new Spice86Creator(
            binName: filePath,
            enableCfgCpu: true,
            enablePit: true,
            recordData: false,
            maxCycles: 50000000, // Increased for DMA processing
            installInterruptVectors: true,
            failOnUnhandledPort: false,
            verboseLogs: true  // Enable verbose logging to see what's happening
        ).Create();
        
        // Create test handler to capture result - don't pass a logger, just use the emulator's internal one
        DebugTestHandler testHandler = new(_output, spice86.Machine.CpuState, spice86.Machine.IoPortDispatcher);
        
        _output.WriteLine("Starting emulator...");
        _output.WriteLine($"Initial DAC channel enabled: {spice86.Machine.SoundBlaster.DacChannel.IsEnabled}");
        _output.WriteLine($"Initial DAC channel sample rate: {spice86.Machine.SoundBlaster.DacChannel.GetSampleRate()}");
        
        // Run the program
        spice86.ProgramExecutor.Run();
        
        _output.WriteLine($"Emulator stopped after {spice86.Machine.Cpu.State.Cycles} cycles");
        _output.WriteLine($"Final DAC channel enabled: {spice86.Machine.SoundBlaster.DacChannel.IsEnabled}");
        _output.WriteLine($"IRQ pending 8-bit: {spice86.Machine.SoundBlaster.PendingIrq8Bit}");
        _output.WriteLine($"IRQ pending 16-bit: {spice86.Machine.SoundBlaster.PendingIrq16Bit}");
        
        // Wait for mixer thread to process
        _output.WriteLine("Waiting 1 second for mixer thread...");
        await System.Threading.Tasks.Task.Delay(1000);
        
        _output.WriteLine($"After wait - IRQ pending 8-bit: {spice86.Machine.SoundBlaster.PendingIrq8Bit}");
        _output.WriteLine($"Test results: {string.Join(", ", testHandler.Results.Select(r => $"0x{r:X2}"))}");
        
        if (testHandler.Results.Contains(0x00)) {
            _output.WriteLine("TEST PASSED - IRQ was signaled!");
        } else if (testHandler.Results.Contains(0xFF)) {
            _output.WriteLine("TEST FAILED - IRQ timeout!");
        } else {
            _output.WriteLine("TEST INCOMPLETE - No result written");
        }
    }
    
    private class DebugTestHandler : DefaultIOPortHandler {
        private const int ResultPort = 0x999;
        private readonly ITestOutputHelper _output;
        public List<byte> Results { get; } = new();
        
        public DebugTestHandler(ITestOutputHelper output, State state, IOPortDispatcher ioPortDispatcher) 
            : base(state, true, null!) {  // Pass null for logger, we'll use _output directly
            _output = output;
            ioPortDispatcher.AddIOPortHandler(ResultPort, this);
        }
        
        public override void WriteByte(ushort port, byte value) {
            if (port == ResultPort) {
                _output?.WriteLine($"Result written to port 0x999: 0x{value:X2} ({(value == 0 ? "SUCCESS" : "FAILURE")})");
                Results.Add(value);
            }
        }
    }
}
