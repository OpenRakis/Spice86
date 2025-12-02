namespace Spice86.Tests;

using FluentAssertions;
using Xunit;

/// <summary>
/// Stress tests for the startup path to expose race conditions between
/// the emulator thread and the UI rendering thread.
/// </summary>
public class StartupStressTests {
    /// <summary>
    /// Repeatedly starts and stops the emulator to expose race conditions during initialization.
    /// This test aims to reproduce the NullReferenceException in Renderer.DrawTextMode
    /// that occurs when rendering happens before VGA state is fully initialized.
    /// </summary>
    [Fact]
    public void StartupStress_RepeatedStartStop_NoRaceConditions() {
        // Run multiple iterations to increase chance of hitting race condition
        const int iterations = 20;
        
        for (int i = 0; i < iterations; i++) {
            using Spice86DependencyInjection spice86 = new Spice86Creator(
                "add",
                enableCfgCpu: true,
                maxCycles: 1000  // Enough cycles to complete but still quick
            ).Create();
            
            // Run the emulator briefly
            spice86.ProgramExecutor.Run();
        }
    }
    
    /// <summary>
    /// Tests with different test binaries to vary initialization patterns.
    /// </summary>
    [Theory]
    [InlineData("add")]
    [InlineData("interrupt")]
    [InlineData("bitwise")]
    public void StartupStress_DifferentBinaries_NoRaceConditions(string binName) {
        const int iterations = 10;
        
        for (int i = 0; i < iterations; i++) {
            using Spice86DependencyInjection spice86 = new Spice86Creator(
                binName,
                enableCfgCpu: true,
                maxCycles: 1000
            ).Create();
            
            spice86.ProgramExecutor.Run();
        }
    }
}
