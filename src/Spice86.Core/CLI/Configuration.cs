namespace Spice86.Core.CLI;

using CommandLine;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Function;

/// <summary> Configuration for spice86, that is what to run and how. Set on startup. </summary>
public sealed class Configuration {
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
    [Option('e', nameof(Exe), Required = true, HelpText = "Path to executable")]
    public string Exe { get; set; } = string.Empty;

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
    /// Directory to dump data to when not specified otherwise. If blank dumps to SPICE86_DUMPS_FOLDER, and if not defined dumps to a sub directory named with the program SHA 256 signature.
    /// </summary>
    [Option('r', nameof(RecordedDataDirectory), Required = false, HelpText = "Directory to dump data to when not specified otherwise. If blank dumps to SPICE86_DUMPS_FOLDER, and if not defined dumps to a sub directory named with the program SHA 256 signature")]
    public string? RecordedDataDirectory { get; init; }

    /// <summary>
    /// Install DOS interrupt vectors or not.
    /// </summary>
    [Option('v', nameof(InitializeDOS), Default = null, Required = false, HelpText = "Install DOS interrupt vectors or not")]
    public bool? InitializeDOS { get; set; }

    /// <summary>
    /// Only for <see cref="PitTimer"/>
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
    [Option('p', "ProgramEntryPointSegment", Required = false, Default = "0x170", HelpText = "Segment where to load the program. DOS PSP and MCB will be created before it.")]
    public string? ProgramEntryPointSegmentString { get => null; set => ProgramEntryPointSegment = CommandLineParser.ParseHexDecBinUInt16(value!); }
    public ushort ProgramEntryPointSegment;

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
    /// Audio engine to use
    /// </summary>
    [Option(nameof(AudioEngine), Default = AudioEngine.CrossPlatform, Required = false, HelpText = "Audio engine to use. CrossPlatform uses WASAPI on Windows and SDL on other platforms. Values are CrossPlatform or Dummy")]
    public AudioEngine AudioEngine { get; init; }

    /// <summary>
    /// Select the OPL synthesis mode.
    /// </summary>
    [Option(nameof(OplMode), Default = OplMode.Opl3, Required = false,
        HelpText = "OPL synthesis mode. Values are None, Opl2, DualOpl2, Opl3, Opl3Gold. Default is Opl3.")]
    public OplMode OplMode { get; init; }

    /// <summary>
    /// Sound Blaster type to emulate.
    /// </summary>
    [Option(nameof(SbType), Default = SbType.SBPro2, Required = false,
        HelpText = "Sound Blaster card type. Values are None, SB1, SB2, SBPro1, SBPro2, Sb16, GameBlaster. Default is SBPro2.")]
    public SbType SbType { get; init; }

    /// <summary>
    /// Sound Blaster base I/O address.
    /// </summary>
    [Option(nameof(SbBase), Default = (ushort)0x220, Required = false,
        HelpText = "Sound Blaster base I/O address (hex). Default is 0x220. Common values: 0x220, 0x240, 0x260, 0x280.")]
    public ushort SbBase { get; init; }

    /// <summary>
    /// Sound Blaster IRQ line.
    /// </summary>
    [Option(nameof(SbIrq), Default = (byte)7, Required = false,
        HelpText = "Sound Blaster IRQ line. Default is 7. Common values: 5, 7, 9, 10.")]
    public byte SbIrq { get; init; }

    /// <summary>
    /// Sound Blaster 8-bit DMA channel.
    /// </summary>
    [Option(nameof(SbDma), Default = (byte)1, Required = false,
        HelpText = "Sound Blaster 8-bit DMA channel. Default is 1. Common values: 0, 1, 3.")]
    public byte SbDma { get; init; }

    /// <summary>
    /// Sound Blaster 16-bit high DMA channel.
    /// </summary>
    [Option(nameof(SbHdma), Default = (byte)5, Required = false,
        HelpText = "Sound Blaster 16-bit high DMA channel. Default is 5. Common values: 5, 6, 7.")]
    public byte SbHdma { get; init; }

    [Option(nameof(Xms), Default = null, Required = false, HelpText = "Enable XMS. Default is true.")]
    public bool? Xms { get; init; }

    /// <summary>
    /// If true, will throw an exception and crash when encountering an invalid opcode.
    /// If false, will handle invalid opcodes as CPU faults (int 0x06).
    /// Default is true because usually invalid opcode means emulator bug.
    /// </summary>
    [Option(nameof(FailOnInvalidOpcode), Default = true, Required = false,
        HelpText = "If true, will throw an exception and crash when encountering an invalid opcode. If false, will handle invalid opcodes as CPU faults. Default is true.")]
    public bool FailOnInvalidOpcode { get; init; }

    /// <summary>
    /// If true, logs every executed instruction to a file (similar to DOSBox heavy logging).
    /// This will significantly impact performance. Default is false.
    /// </summary>
    [Option(nameof(CpuHeavyLog), Default = false, Required = false,
        HelpText = "Enable CPU heavy logging. Logs every executed instruction to a file. Warning: significant performance impact.")]
    public bool CpuHeavyLog { get; set; }

    /// <summary>
    /// Custom file path for CPU heavy log output. If not specified, defaults to {DumpDirectory}/cpu_heavy.log
    /// </summary>
    [Option(nameof(CpuHeavyLogDumpFile), Default = null, Required = false,
        HelpText = "Custom file path for CPU heavy log output. If not specified, defaults to {DumpDirectory}/cpu_heavy.log")]
    public string? CpuHeavyLogDumpFile { get; init; }
    
    [Option(nameof(AsmRenderingStyle), Default = AsmRenderingStyle.Spice86, Required = false,
        HelpText = "Style of the ASM rendering. Spice86 or DosBox.")]
    public AsmRenderingStyle AsmRenderingStyle { get; init; }
}