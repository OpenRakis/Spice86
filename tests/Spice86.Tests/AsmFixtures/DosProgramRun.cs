namespace Spice86.Tests.AsmFixtures;

using Spice86.Shared.Utils;

using Spice86.Tests.Utility;

/// <summary>
/// Per-run host directory of a constructed DOS program scenario: it creates the files, folders and
/// resource copies the program needs inside a directory no other run shares, and builds the fixture that
/// runs one entry file with the standard batch-routing machine configuration. The scenario owns the
/// oracle; the helpers here only prepare the run and read back what the program left behind.
/// </summary>
internal sealed class DosProgramRun {
    /// <summary>File name of the batch file most constructed scenarios execute.</summary>
    public const string StartBatchFileName = "START.BAT";

    private readonly AsmRunScope _scope;
    private readonly string _identity;

    /// <summary>Creates the run directory of a scenario.</summary>
    /// <param name="scope">Scope owning this run's directory and observations.</param>
    /// <param name="identity">Stable scenario identity, used as the directory name prefix.</param>
    public DosProgramRun(AsmRunScope scope, string identity) {
        _scope = scope;
        _identity = identity;
        HostDirectory = scope.CreateWorkingDirectory(identity);
    }

    /// <summary>Host directory holding this run's files. No other run of any scenario sees it.</summary>
    public string HostDirectory { get; }

    /// <summary>Writes a binary file at the root of the run directory.</summary>
    /// <param name="fileName">File name, usually an 8.3 DOS name.</param>
    /// <param name="content">File content.</param>
    /// <returns>This run, for further preparation.</returns>
    public DosProgramRun Binary(string fileName, byte[] content) {
        File.WriteAllBytes(PathOf(fileName), content);
        return this;
    }

    /// <summary>Writes a text file at the root of the run directory.</summary>
    /// <param name="fileName">File name, usually an 8.3 DOS name.</param>
    /// <param name="content">File content.</param>
    /// <returns>This run, for further preparation.</returns>
    public DosProgramRun Text(string fileName, string content) {
        File.WriteAllText(PathOf(fileName), content);
        return this;
    }

    /// <summary>Writes a binary file inside a subdirectory of the run directory, creating it if needed.</summary>
    /// <param name="relativeDirectory">Directory path relative to the run directory.</param>
    /// <param name="fileName">File name.</param>
    /// <param name="content">File content.</param>
    /// <returns>This run, for further preparation.</returns>
    public DosProgramRun BinaryIn(string relativeDirectory, string fileName, byte[] content) {
        File.WriteAllBytes(Path.Join(FolderPath(relativeDirectory), fileName), content);
        return this;
    }

    /// <summary>Writes a text file inside a subdirectory of the run directory, creating it if needed.</summary>
    /// <param name="relativeDirectory">Directory path relative to the run directory.</param>
    /// <param name="fileName">File name.</param>
    /// <param name="content">File content.</param>
    /// <returns>This run, for further preparation.</returns>
    public DosProgramRun TextIn(string relativeDirectory, string fileName, string content) {
        File.WriteAllText(Path.Join(FolderPath(relativeDirectory), fileName), content);
        return this;
    }

    /// <summary>Creates a subdirectory of the run directory, for further preparation.</summary>
    /// <param name="relativeDirectory">Directory path relative to the run directory.</param>
    /// <returns>This run, for further preparation.</returns>
    public DosProgramRun Folder(string relativeDirectory) {
        Directory.CreateDirectory(PathOf(relativeDirectory));
        return this;
    }

    /// <summary>Creates a subdirectory of the run directory and resolves its host path.</summary>
    /// <param name="relativeDirectory">Directory path relative to the run directory.</param>
    /// <returns>The created directory path.</returns>
    private string FolderPath(string relativeDirectory) {
        return Folder(relativeDirectory).PathOf(relativeDirectory);
    }

    /// <summary>Copies a committed test resource into the run directory under another file name.</summary>
    /// <param name="resourceSubDirectory">Resource folder under the test resources root.</param>
    /// <param name="fileName">File name inside that folder.</param>
    /// <param name="targetFileName">File name to copy it to in the run directory.</param>
    /// <returns>This run, for further preparation.</returns>
    public DosProgramRun ResourceCopy(string resourceSubDirectory, string fileName, string targetFileName) {
        string source = Path.Join(AppContext.BaseDirectory, "Resources", resourceSubDirectory, fileName);
        File.Copy(source, PathOf(targetFileName), overwrite: true);
        return this;
    }

    /// <summary>Stores an observation on the run's scope, so the oracle can read it afterwards.</summary>
    /// <param name="name">Name of the observation.</param>
    /// <param name="observation">Observation to store.</param>
    public void RegisterObservation(string name, object observation) {
        _scope.Register(name, observation);
    }

    /// <summary>Resolves a path inside this run's directory.</summary>
    /// <param name="relativePath">Path relative to the run directory.</param>
    /// <returns>The rooted host path.</returns>
    public string PathOf(string relativePath) {
        return Path.Join(HostDirectory, relativePath);
    }

    /// <summary>Reads a host text file this run's program left behind.</summary>
    /// <param name="relativePath">Path relative to the run directory.</param>
    /// <returns>The file content.</returns>
    public string TextFile(string relativePath) {
        return File.ReadAllText(PathOf(relativePath));
    }

    /// <summary>Whether the program created a host file in this run's directory.</summary>
    /// <param name="relativePath">Path relative to the run directory.</param>
    /// <returns>true when the file exists.</returns>
    public bool HostFileExists(string relativePath) {
        return File.Exists(PathOf(relativePath));
    }

    /// <summary>Whether the program left a host directory in this run's directory.</summary>
    /// <param name="relativePath">Path relative to the run directory.</param>
    /// <returns>true when the directory exists.</returns>
    public bool HostDirectoryExists(string relativePath) {
        return Directory.Exists(PathOf(relativePath));
    }

    /// <summary>The run settings of a constructed DOS program: the PIT runs, interrupt vectors are
    /// installed, the cycle failsafe is the batch budget and the run directory is drive C:.</summary>
    /// <param name="entryFileName">Entry file name or path relative to the run directory.</param>
    /// <returns>Settings with the entry file and the standard machine configuration.</returns>
    public AsmRunSettings Settings(string entryFileName) {
        return SettingsForHostPath(PathOf(entryFileName));
    }

    /// <summary>The standard run settings with a host path that is not inside the run directory, for the
    /// few scenarios that run a differently named entry file.</summary>
    /// <param name="hostPath">Rooted host path of the entry file.</param>
    /// <returns>Settings with the entry file and the standard machine configuration.</returns>
    public AsmRunSettings SettingsForHostPath(string hostPath) {
        return new AsmRunSettings {
            BinName = hostPath,
            ScenarioName = _identity,
            EnablePit = true,
            MaxCycles = 300000L,
            InstallInterruptVectors = true,
            CDrive = HostDirectory
        };
    }

    /// <summary>Builds the fixture that runs the entry file with the standard settings.</summary>
    /// <param name="entryFileName">Entry file name or path relative to the run directory.</param>
    /// <param name="assertResult">The scenario's one oracle.</param>
    /// <returns>The fixture to run.</returns>
    public AsmFixture Run(string entryFileName, Action<AsmFixtureResult> assertResult) {
        return new AsmFixture(Settings(entryFileName), [], assertResult);
    }

    /// <summary>Builds the fixture that runs a START.BAT holding the given script, the entry file of most
    /// batch routing scenarios.</summary>
    /// <param name="script">Content of the START.BAT batch file.</param>
    /// <param name="assertResult">The scenario's one oracle.</param>
    /// <returns>The fixture to run.</returns>
    public AsmFixture StartBatch(string script, Action<AsmFixtureResult> assertResult) {
        Text(StartBatchFileName, script);
        return Run(StartBatchFileName, assertResult);
    }

    /// <summary>Builds the fixture that runs a START.BAT holding the given script with key codes preloaded
    /// into the BIOS keyboard buffer before the program starts.</summary>
    /// <param name="script">Content of the START.BAT batch file.</param>
    /// <param name="keyCodes">BIOS key codes to preload, in reading order.</param>
    /// <param name="assertResult">The scenario's one oracle.</param>
    /// <returns>The fixture to run.</returns>
    public AsmFixture StartBatch(string script, IReadOnlyList<ushort> keyCodes,
        Action<AsmFixtureResult> assertResult) {
        Text(StartBatchFileName, script);
        return RunWithKeys(StartBatchFileName, keyCodes, assertResult);
    }

    /// <summary>Builds the fixture that runs the entry file without the PIT, for scripts whose timing must
    /// stay instruction driven.</summary>
    /// <param name="entryFileName">Entry file name or path relative to the run directory.</param>
    /// <param name="assertResult">The scenario's one oracle.</param>
    /// <returns>The fixture to run.</returns>
    public AsmFixture RunWithoutPit(string entryFileName, Action<AsmFixtureResult> assertResult) {
        return new AsmFixture(Settings(entryFileName) with { EnablePit = false }, [], assertResult);
    }

    /// <summary>Builds the fixture that runs the entry file with key codes preloaded into the BIOS keyboard
    /// buffer before the program starts.</summary>
    /// <param name="entryFileName">Entry file name or path relative to the run directory.</param>
    /// <param name="keyCodes">BIOS key codes to preload, in reading order.</param>
    /// <param name="assertResult">The scenario's one oracle.</param>
    /// <returns>The fixture to run.</returns>
    public AsmFixture RunWithKeys(string entryFileName, IReadOnlyList<ushort> keyCodes,
        Action<AsmFixtureResult> assertResult) {
        return new AsmFixture(Settings(entryFileName) with { PreloadedKeyCodes = keyCodes }, [], assertResult);
    }

    /// <summary>Builds the fixture that runs the entry file with host command line arguments.</summary>
    /// <param name="entryFileName">Entry file name or path relative to the run directory.</param>
    /// <param name="arguments">Command line arguments of the program.</param>
    /// <param name="assertResult">The scenario's one oracle.</param>
    /// <returns>The fixture to run.</returns>
    public AsmFixture RunWithArguments(string entryFileName, string arguments,
        Action<AsmFixtureResult> assertResult) {
        return new AsmFixture(Settings(entryFileName) with { ExeArgs = arguments }, [], assertResult);
    }

    /// <summary>Builds the fixture that runs the entry file with explicitly modified settings, for the few
    /// scenarios that deviate from the standard machine configuration.</summary>
    /// <param name="settings">Settings to run with, usually this run's settings with a modification.</param>
    /// <param name="assertResult">The scenario's one oracle.</param>
    /// <returns>The fixture to run.</returns>
    public AsmFixture RunCustomSettings(AsmRunSettings settings, Action<AsmFixtureResult> assertResult) {
        return new AsmFixture(settings, [], assertResult);
    }

    /// <summary>Reads the text characters of the first video cells of a finished run.</summary>
    /// <param name="result">Result of the finished run.</param>
    /// <param name="count">Number of cells to read.</param>
    /// <returns>The cell characters in display order.</returns>
    public static string Cells(AsmFixtureResult result, int count) {
        return VideoMemoryReader.ReadCells(result.Memory, count);
    }

    /// <summary>Reads one text character of the first video cells of a finished run.</summary>
    /// <param name="result">Result of the finished run.</param>
    /// <param name="index">Zero-based cell index.</param>
    /// <returns>The character in that cell.</returns>
    public static char Cell(AsmFixtureResult result, int index) {
        return Cells(result, index + 1)[index];
    }

    /// <summary>Reads the first rows of the 80 column text screen of a finished run.</summary>
    /// <param name="result">Result of the finished run.</param>
    /// <returns>The screen text.</returns>
    public static string ScreenText(AsmFixtureResult result) {
        return Cells(result, 80 * 8);
    }

    /// <summary>Reads a linear memory byte of a finished run.</summary>
    /// <param name="result">Result of the finished run.</param>
    /// <param name="address">Linear address to read.</param>
    /// <returns>The byte at that address.</returns>
    public static byte ByteAt(AsmFixtureResult result, uint address) {
        return result.Memory.UInt8[address];
    }

    /// <summary>Reads one text video cell character as its raw byte, for programs that report a status
    /// byte rather than a printable character.</summary>
    /// <param name="result">Result of the finished run.</param>
    /// <param name="index">Zero-based cell index.</param>
    /// <returns>The character byte in that cell.</returns>
    public static byte VideoByte(AsmFixtureResult result, int index) {
        return result.Memory.UInt8[MemoryUtils.ToPhysicalAddress(0xB800, (ushort)(index * 2))];
    }
}
