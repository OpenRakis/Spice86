namespace Spice86.Tests.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;

/// <summary>
/// Shared test fixture for DOS components (FileManager, FcbManager, etc.)
/// Provides a fully wired DOS environment for testing.
/// </summary>
public class DosTestFixture : IDisposable {
    private readonly Spice86DependencyInjection _spice86;
    private readonly string _tempExeDir;

    public DosFileManager DosFileManager => _spice86.Machine.Dos.FileManager;
    public DosFcbManager DosFcbManager => _spice86.Machine.Dos.FcbManager;
    public Dos Dos => _spice86.Machine.Dos;
    public IMemory Memory => _spice86.Machine.Memory;
    public State CpuState => _spice86.Machine.CpuState;
    public DosInt21Handler DosInt21Handler => _spice86.Machine.Dos.DosInt21Handler;

    public DosTestFixture(string mountPoint) {
        _tempExeDir = Path.Join(Path.GetTempPath(), $"DosTestFixture_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempExeDir);
        string exePath = Path.Join(_tempExeDir, "EXIT.COM");
        // MOV AX, 4C00h / INT 21h — minimal DOS exit stub
        File.WriteAllBytes(exePath, new byte[] { 0xB8, 0x00, 0x4C, 0xCD, 0x21 });
        _spice86 = new Spice86Creator(
            binName: exePath,
            installInterruptVectors: true,
            cDrive: mountPoint).Create();
    }

    public void Dispose() {
        _spice86.Dispose();
        if (Directory.Exists(_tempExeDir)) {
            Directory.Delete(_tempExeDir, true);
        }
    }
}
