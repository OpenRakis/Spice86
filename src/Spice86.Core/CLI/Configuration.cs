namespace Spice86.Core.CLI;

using CommandLine;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Function;
using Spice86.Shared.Utils;

using System.Security.Cryptography;

/// <summary> Configuration for spice86, that is what to run and how. Set on startup. </summary>
public sealed class Configuration {
    /// <summary>
    /// Gets the SHA 256 signature of the target <see cref="Exe"/>
    /// </summary>
    public string ProgramHash { get; internal set; } = "";

    /// <summary>
    /// CPU cycles per ms. Can be more precisely specified than instructions per second. Overrides instructions per second if used.
    /// </summary>
    [Option(nameof(Cycles), Default = null, Required = false, HelpText = "Precise control of the number of emulated CPU cycles per ms. For the rare speed-sensitive game. Default is undefined. Overrides instructions per second option if used.")]
    public int? Cycles { get; init; }
    
    /// <summary>
    /// Cpu Model to emulate
    /// </summary>
    [Option(nameof(CpuModel), Default = CpuModel.INTEL_80286, Required = false, HelpText = "Cpu Model to emulate")]
    public CpuModel CpuModel { get; init; }

    /// <summary>
    /// Gets if the A20 gate is silenced. If <c>true</c> memory addresses will rollover above 1 MB.
    /// </summary>
    [Option(nameof(A20Gate), Default = false, Required = false, HelpText = "Whether the 20th address line is silenced. Used for legacy 8086 programs.")]
    public bool A20Gate { get; init; }
    
    /// <summary>
    /// Gets if the program will be paused on start and stop. If <see cref="GdbPort"/> is set, the program will be paused anyway.
    /// </summary>
    [Option(nameof(Debug), Default = false, Required = false, HelpText = "Starts the program paused and pauses once again when stopping.")]
    public bool Debug { get; init; }

    /// <summary> Path to C drive, default is exe parent. </summary>
    [Option('c', nameof(CDrive), Default = null, Required = false, HelpText = "Path to C drive, default is exe parent")]
    public string? CDrive { get; set; }

    /// <summary> Path to executable. </summary>
    [Option('e', nameof(Exe), Default = null, Required = false, HelpText = "Path to executable")]
    public string? Exe { get; set; }

    /// <summary> List of parameters to give to the emulated program. </summary>
    [Option('a', nameof(ExeArgs), Default = null, Required = false, HelpText = "List of parameters to give to the emulated program")]
    public string? ExeArgs { get; set; }

    /// <summary> Hexadecimal string representing the expected SHA256 checksum of the emulated program. </summary>
    [Option('x', nameof(ExpectedChecksum), Default = null, Required = false, HelpText = "Hexadecimal string representing the expected SHA256 checksum of the emulated program")]
    public string? ExpectedChecksum { get; init; }

    /// <summary> The value of the expected SHA256 checksum. </summary>
    public byte[] ExpectedChecksumValue { get; set; } = Array.Empty<byte>();

    /// <summary> Instantiated <see cref="OverrideSupplierClassName"/>. Created by <see cref="CommandLineParser"/>. </summary>
    public IOverrideSupplier? OverrideSupplier { get; set; }

    /// <summary> Name of a class in the current folder that will generate the initial function information. See documentation for more information. </summary>
    [Option('o', nameof(OverrideSupplierClassName), Default = null, Required = false, HelpText = "Name of a class in the current folder that will generate the initial function information. See documentation for more information.")]
    public string? OverrideSupplierClassName { get; init; }

    /// <summary> If false it will use the names provided by <see cref="OverrideSupplierClassName"/> but not the code. Use this in the code for <see cref="UseCodeOverride"/>. </summary>
    [Option('u', nameof(UseCodeOverride), Default = null, Required = false, HelpText = "<true or false> if false it will use the names provided by overrideSupplierClassName but not the code")]
    public bool? UseCodeOverride { get; set; }

    /// <summary> Gets a value indicating whether the <see cref="UseCodeOverride"/> option is set to true. </summary>
    public bool UseCodeOverrideOption => UseCodeOverride ?? true;

    /// <summary>
    ///     Flag indicating if headless mode is enabled. Using this option without a value sets it to Default.
    /// </summary>
    [Option('h', nameof(HeadlessMode), Default = null, Required = false,
        HelpText =
            "Headless mode. 'Minimal' does not use any UI components, 'Avalonia' uses the full UI and consumes a bit more memory.")]
    public HeadlessType? HeadlessMode { get; init; }

    /// <summary> When true, records data at runtime and dumps them at exit time. </summary>
    [Option('d', nameof(DumpDataOnExit), Default = null, Required = false, HelpText = "When true, records data at runtime and dumps them at exit time")]
    public bool? DumpDataOnExit { get; set; }

    /// <summary>
    /// If true, will fail when encountering an unhandled IO port. Useful to check for unimplemented hardware. false by default.
    /// </summary>
    [Option('f', nameof(FailOnUnhandledPort), Default = false, Required = false, HelpText = "If true, will fail when encountering an unhandled IO port. Useful to check for unimplemented hardware. false by default.")]
    public bool FailOnUnhandledPort { get; init; }

    /// <summary>
    /// GDB port spice86 will listen to.
    /// </summary>
    [Option('g', nameof(GdbPort), Default = 10000, Required = false, HelpText = "GDB port. If 0, GDB server will be disabled.")]
    public int GdbPort { get; init; }

    /// <summary>
    /// Directory to dump data to when not specified otherwise. If blank dumps to SPICE86_DUMPS_FOLDER, and if not defined dumps to a sub directory named with the <see cref="ProgramHash"/>.
    /// </summary>
    [Option('r', nameof(RecordedDataDirectory), Required = false, HelpText = "Directory to dump data to when not specified otherwise. If blank dumps to SPICE86_DUMPS_FOLDER, and if not defined dumps to a sub directory named with the program SHA 256 signature")]
    public string RecordedDataDirectory { get; internal set; } = "";

    /// <summary>
    /// Install DOS interrupt vectors or not.
    /// </summary>
    [Option('v', nameof(InitializeDOS), Default = null, Required = false, HelpText = "Install DOS interrupt vectors or not")]
    public bool? InitializeDOS { get; set; }

    /// <summary>
    /// Only for <see cref="Timer"/>
    /// </summary>
    [Option('i', nameof(InstructionsPerSecond), Required = false, HelpText = "<number of instructions that have to be executed by the emulator to consider a second passed> if blank will use time based timer.")]
    public long? InstructionsPerSecond { get; set; }

    /// <summary>
    /// The time multiplier used for speeding up or slowing down the execution of the program.
    /// </summary>
    [Option('t', nameof(TimeMultiplier), Default = 1, Required = false, HelpText = "<time multiplier> if >1 will go faster, if <1 will go slower.")]
    public double TimeMultiplier { get; init; }

    /// <summary>
    /// The memory segment where the program will be loaded. The DOS PSP (Program Segment Prefix) and MCB (Memory Control Block) will be created before it.
    /// </summary>
    [Option('p', nameof(ProgramEntryPointSegment), Default = (ushort)0x170, Required = false, HelpText = "Segment where to load the program. DOS PSP and MCB will be created before it.")]
    public ushort ProgramEntryPointSegment { get; init; }


    /// <summary>
    /// The memory address where the ASM handlers for interrupts and so on are to be written. Default is F000 which is the bios segment. Not all games will be happy with this changed to something else.
    /// </summary>
    [Option(nameof(ProvidedAsmHandlersSegment), Default = (ushort)0xF000, Required = false, HelpText = "Memory address where the ASM handlers for interrupts and so on are to be written. Default is F000 which is the bios segment. Not all games will be happy with this changed to something else.")]
    public ushort ProvidedAsmHandlersSegment { get; init; }

    /// <summary>
    /// Determines whether logs should be silenced or not.
    /// </summary>
    [Option('s', nameof(SilencedLogs), Default = false, Required = false, HelpText = "Disable all logs")]
    public bool SilencedLogs { get; init; }

    /// <summary>
    /// Determines whether verbose level logs should be enabled or not.
    /// </summary>
    [Option('l', nameof(VerboseLogs), Default = false, Required = false, HelpText = "Enable verbose level logs")]
    public bool VerboseLogs { get; init; }

    /// <summary>
    /// Determines whether warning level logs should be enabled or not.
    /// </summary>
    [Option('w', nameof(WarningLogs), Default = false, Required = false, HelpText = "Enable warning level logs")]
    public bool WarningLogs { get; init; }

    /// <summary>
    /// The path to the zip file or directory containing the MT-32 ROM files.
    /// </summary>
    [Option('m', nameof(Mt32RomsPath), Default = null, Required = false, HelpText = "Zip file or directory containing the MT-32 ROM files")]
    public string? Mt32RomsPath { get; init; }

    /// <summary>
    /// Determines whether EMS (Expanded Memory Specification) should be enabled or not.
    /// </summary>
    [Option(nameof(Ems), Default = null, Required = false, HelpText = "Enable EMS")]
    public bool? Ems { get; init; }

    /// <summary>
    /// Specify the type of mouse to use.
    /// </summary>
    [Option(nameof(Mouse), Default = MouseType.Ps2, Required = false, HelpText = "Specify the type of mouse to use. Valid values are None, PS2 (default), and PS2Wheel")]
    public MouseType Mouse { get; init; }

    /// <summary>
    /// Specify a C header file to be used for structure information
    /// </summary>
    [Option(nameof(StructureFile), Default = null, Required = false, HelpText = "Specify a C header file to be used for structure information")]
    public string? StructureFile { get; init; }
    
    /// <summary>
    /// Determines whether to use experimental CFG CPU or regular interpreter.
    /// </summary>
    [Option(nameof(CfgCpu), Default = false, Required = false, HelpText = "Enable CFG CPU (Control Flow Graph)")]
    public bool CfgCpu { get; init; }

    /// <summary>
    /// Audio engine to use
    /// </summary>
    [Option(nameof(AudioEngine), Default = AudioEngine.PortAudio, Required = false, HelpText = "Audio engine to use. Values are PortAudio or Dummy")]
    public AudioEngine AudioEngine { get; init; }

    [Option(nameof(Xms), Default = null, Required = false, HelpText = "Enable XMS. Default is true.")]
    public bool? Xms { get; init; }
}