namespace Spice86.Emulator;

using Spice86.Emulator.Function;

/// <summary> Configuration for spice86, that is what to run and how. </summary>
public class Configuration {
    private string? _cDrive;

    private string? _defaultDumpDirectory;

    private string? _exe;

    private string? _exeArgs;

    private byte[] _expectedChecksum = System.Array.Empty<byte>();

    private bool _failOnUnhandledPort;

    private int? _gdbPort;

    private bool _installInterruptVector;

    private IOverrideSupplier? _overrideSupplier;

    private int _programEntryPointSegment;

    private bool _useCodeOverride;

    // Only for timer
    private long instructionsPerSecond;

    private double timeMultiplier;

    public string? GetcDrive() {
        return _cDrive;
    }

    public string? GetDefaultDumpDirectory() {
        return _defaultDumpDirectory;
    }

    public string? GetExe() {
        return _exe;
    }

    public string? GetExeArgs() {
        return _exeArgs;
    }

    public byte[] GetExpectedChecksum() {
        return _expectedChecksum;
    }

    public int? GetGdbPort() {
        return _gdbPort;
    }

    public long GetInstructionsPerSecond() {
        return instructionsPerSecond;
    }

    public IOverrideSupplier? GetOverrideSupplier() {
        return _overrideSupplier;
    }

    public int GetProgramEntryPointSegment() {
        return _programEntryPointSegment;
    }

    public double GetTimeMultiplier() {
        return timeMultiplier;
    }

    public bool IsFailOnUnhandledPort() {
        return _failOnUnhandledPort;
    }

    public bool IsInstallInterruptVector() {
        return _installInterruptVector;
    }

    public bool IsUseCodeOverride() {
        return _useCodeOverride;
    }

    public void SetcDrive(string cDrive) {
        _cDrive = cDrive;
    }

    public void SetDefaultDumpDirectory(string defaultDumpDirectory) {
        _defaultDumpDirectory = defaultDumpDirectory;
    }

    public void SetExe(string? exe) {
        _exe = exe;
    }

    public void SetExeArgs(string exeArgs) {
        _exeArgs = exeArgs;
    }

    public void SetExpectedChecksum(byte[] expectedChecksum) {
        _expectedChecksum = expectedChecksum;
    }

    public void SetFailOnUnhandledPort(bool failOnUnhandledPort) {
        _failOnUnhandledPort = failOnUnhandledPort;
    }

    public void SetGdbPort(int gdbPort) {
        _gdbPort = gdbPort;
    }

    public void SetInstallInterruptVector(bool installInterruptVector) {
        _installInterruptVector = installInterruptVector;
    }

    public void SetInstructionsPerSecond(long instructionsPerSecond) {
        this.instructionsPerSecond = instructionsPerSecond;
    }

    public void SetOverrideSupplier(IOverrideSupplier? overrideSupplier) {
        _overrideSupplier = overrideSupplier;
    }

    public void SetProgramEntryPointSegment(int programEntryPointSegment) {
        _programEntryPointSegment = programEntryPointSegment;
    }

    public void SetTimeMultiplier(double timeMultiplier) {
        this.timeMultiplier = timeMultiplier;
    }

    public void SetUseCodeOverride(bool useCodeOverride) {
        _useCodeOverride = useCodeOverride;
    }
}