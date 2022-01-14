namespace Spice86.Emulator.LoadableFile;

using Serilog;

using Spice86.Emulator.Cpu;
using Spice86.Emulator.Machine;
using Spice86.Emulator.Memory;
using Spice86.Utils;

using System.IO;

/// <summary>
/// Base class for loading executable files in the VM like exe, bios, ...
/// </summary>
public abstract class ExecutableFileLoader {
    protected Cpu _cpu;
    protected Machine _machine;
    protected Memory _memory;
    private static readonly ILogger _logger = Log.Logger.ForContext<ExecutableFileLoader>();

    protected ExecutableFileLoader(Machine machine) {
        _machine = machine;
        _cpu = machine.GetCpu();
        _memory = machine.GetMemory();
    }

    public abstract byte[] LoadFile(string file, string arguments);

    protected byte[] ReadFile(string file) {
        return File.ReadAllBytes(file);
    }

    protected void SetEntryPoint(int cs, int ip) {
        State state = _cpu.GetState();
        state.SetCS(cs);
        state.SetIP(ip);
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Program entry point is {@ProgramEntty}", ConvertUtils.ToSegmentedAddressRepresentation(cs, ip));
        }
    }
}