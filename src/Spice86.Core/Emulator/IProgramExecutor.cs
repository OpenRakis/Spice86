namespace Spice86.Core.Emulator;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Debugger;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Shared.Interfaces;

public interface IProgramExecutor : IDisposable, IDebuggableComponent {
    /// <summary>
    /// Starts the emulation process.
    /// </summary>
    /// <param name="cycles">For how many cycles the CPU should run. 0 for infinite (default value). Used for benchmarks.</param>
    void Run(int cycles = 0);
    void DumpEmulatorStateToDirectory(string path);
    bool IsPaused { get; set; }
    bool IsGdbCommandHandlerAvailable { get; }
    void StepInstruction();
}