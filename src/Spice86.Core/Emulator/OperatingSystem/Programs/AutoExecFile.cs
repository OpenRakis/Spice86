namespace Spice86.Core.Emulator.OperatingSystem.Programs;

using Spice86.Core.Emulator.OperatingSystem.Structures;

internal class AutoExecFile : IVirtualFile {
    private readonly Configuration _configuration;

    public AutoExecFile(Configuration configuration) {
        _configuration = configuration;
    }

    public string Name {
        get => "AUTOEXEC.BAT";
        set => throw new InvalidOperationException("Cannot rename a built-in operating system file");
    }

    public IEnumerable<string> GetLines() {
        string? hostFilePath = _configuration.Exe;
        string? arguments = _configuration.ExeArgs;
        string? cDrivePath = _configuration.CDrive;
        string commandLine = $"{Path.GetRelativePath(cDrivePath!, hostFilePath!)} {arguments}";
        yield return commandLine;
    }
}
