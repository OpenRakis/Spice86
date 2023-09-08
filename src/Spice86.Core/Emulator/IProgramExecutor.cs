namespace Spice86.Core.Emulator;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

public interface IProgramExecutor : IDisposable {
    Machine Machine { get; }
    void Run();
    State CpuState { get; }
    IVideoState VideoState { get; }
    ArgbPalette ArgbPalette { get; }
    IVgaRenderer VgaRenderer { get; }
    void DumpEmulatorStateToDirectory(string path);
    bool IsPaused { get; set; }
    IVideoCard VideoCard { get; }
    void SetTimeMultiplier(double value);
}