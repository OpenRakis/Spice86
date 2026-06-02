namespace Spice86.Tests.Dos;

using Spice86.Core.Emulator.CPU;
using NSubstitute;

using Spice86.Core.Emulator.Boot;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Tests.Utility;
using Spice86.Shared.Interfaces;

/// <summary>
/// Shared test fixture for DOS components (FileManager, FcbManager, etc.)
/// Provides a fully wired DOS environment for testing.
/// </summary>
public class DosTestFixture : IDisposable {
    private readonly Spice86Creator _creator;
    private readonly Spice86DependencyInjection _spice86;
    private readonly TempFile _tempExeDir;

    public DosFileManager DosFileManager => _spice86.Machine.Dos.FileManager;
    public DosFcbManager DosFcbManager => _spice86.Machine.Dos.FcbManager;
    public Dos Dos => _spice86.Machine.Dos;
    public IMemory Memory => _spice86.Machine.Memory;
    public State CpuState => _spice86.Machine.CpuState;
    public State State => _spice86.Machine.CpuState;
    public DosDriveManager DriveManager => Dos.DosDriveManager;
    public DosProcessManager ProcessManager => Dos.ProcessManager;
    public FloppyBootService BootService { get; }
    public DosInt21Handler DosInt21Handler => _spice86.Machine.Dos.DosInt21Handler;

    public DosTestFixture(string mountPoint) {
        _tempExeDir = new TempFile("DosTestFixture");
        string exePath = Path.Join(_tempExeDir.Path, "EXIT.COM");
        // MOV AX, 4C00h / INT 21h -- minimal DOS exit stub
        File.WriteAllBytes(exePath, new byte[] { 0xB8, 0x00, 0x4C, 0xCD, 0x21 });
        _creator = new Spice86Creator(
            binName: exePath,
            installInterruptVectors: true,
            cDrive: mountPoint);
        _spice86 = _creator.Create();
        BootService = new FloppyBootService(Memory, _spice86.Machine.CpuState, Substitute.For<ILoggerService>());
    }

    public void Dispose() {
        _spice86.Dispose();
        _creator.Dispose();
        _tempExeDir.Dispose();
    }
}
