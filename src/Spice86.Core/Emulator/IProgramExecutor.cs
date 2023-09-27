namespace Spice86.Core.Emulator;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Debugger;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Shared.Interfaces;

public interface IProgramExecutor : IDisposable, IDebuggableComponent {
    void Run();
    void DumpEmulatorStateToDirectory(string path);
    bool IsPaused { get; set; }
    void Step();
}