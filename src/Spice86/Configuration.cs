namespace Spice86;

using Spice86.Emulator.Function;

/// <summary>
/// Configuration for spice86, that is what to run and how.
/// </summary>
public class Configuration
{
    private string? _exe;
    private string? _exeArgs;
    private string? _cDrive;
    // Only for timer
    private long instructionsPerSecond;
    private double timeMultiplier;
    private int _gdbPort;
    private IOverrideSupplier? _overrideSupplier;
    private bool _useCodeOverride;
    private bool _installInterruptVector;
    private bool _failOnUnhandledPort;
    private int _programEntryPointSegment;
    private byte[] _expectedChecksum = System.Array.Empty<byte>();
    private string? _defaultDumpDirectory;
    public virtual string? GetExe()
    {
        return _exe;
    }

    public virtual void SetExe(string exe)
    {
        _exe = exe;
    }

    public virtual string? GetExeArgs()
    {
        return _exeArgs;
    }

    public virtual void SetExeArgs(string exeArgs)
    {
        _exeArgs = exeArgs;
    }

    public virtual string? GetcDrive()
    {
        return _cDrive;
    }

    public virtual void SetcDrive(string cDrive)
    {
        _cDrive = cDrive;
    }

    public virtual long GetInstructionsPerSecond()
    {
        return instructionsPerSecond;
    }

    public virtual void SetInstructionsPerSecond(long instructionsPerSecond)
    {
        this.instructionsPerSecond = instructionsPerSecond;
    }

    public virtual double GetTimeMultiplier()
    {
        return timeMultiplier;
    }

    public virtual void SetTimeMultiplier(double timeMultiplier)
    {
        this.timeMultiplier = timeMultiplier;
    }

    public virtual int GetGdbPort()
    {
        return _gdbPort;
    }

    public virtual void SetGdbPort(int gdbPort)
    {
        _gdbPort = gdbPort;
    }

    public virtual IOverrideSupplier? GetOverrideSupplier()
    {
        return _overrideSupplier;
    }

    public virtual void SetOverrideSupplier(IOverrideSupplier? overrideSupplier)
    {
        _overrideSupplier = overrideSupplier;
    }

    public virtual bool IsUseCodeOverride()
    {
        return _useCodeOverride;
    }

    public virtual void SetUseCodeOverride(bool useCodeOverride)
    {
        _useCodeOverride = useCodeOverride;
    }

    public virtual bool IsInstallInterruptVector()
    {
        return _installInterruptVector;
    }

    public virtual void SetInstallInterruptVector(bool installInterruptVector)
    {
        _installInterruptVector = installInterruptVector;
    }

    public virtual bool IsFailOnUnhandledPort()
    {
        return _failOnUnhandledPort;
    }

    public virtual void SetFailOnUnhandledPort(bool failOnUnhandledPort)
    {
        _failOnUnhandledPort = failOnUnhandledPort;
    }

    public virtual int GetProgramEntryPointSegment()
    {
        return _programEntryPointSegment;
    }

    public virtual void SetProgramEntryPointSegment(int programEntryPointSegment)
    {
        _programEntryPointSegment = programEntryPointSegment;
    }

    public virtual byte[] GetExpectedChecksum()
    {
        return _expectedChecksum;
    }

    public virtual void SetExpectedChecksum(byte[] expectedChecksum)
    {
        _expectedChecksum = expectedChecksum;
    }

    public virtual string? GetDefaultDumpDirectory()
    {
        return _defaultDumpDirectory;
    }

    public virtual void SetDefaultDumpDirectory(string defaultDumpDirectory)
    {
        _defaultDumpDirectory = defaultDumpDirectory;
    }
}
