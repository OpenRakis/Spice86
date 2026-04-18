namespace Spice86.Core.CLI;

using Spectre.Console.Cli;

using Spice86.Audio.Filters;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Function;

using System.ComponentModel;

/// <summary> Configuration for spice86, that is what to run and how. Set on startup. </summary>
public sealed class Configuration : CommandSettings {
    /// <summary>
    /// CPU cycles per ms. Can be more precisely specified than instructions per second. Overrides instructions per second if used.
    /// </summary>
    [CommandOption("--Cycles <CYCLES>")]
    public int? Cycles { get; init; }

    /// <summary>
    /// Cpu Model to emulate
    /// </summary>
    [CommandOption("--CpuModel <CPUMODEL>")]
    [DefaultValue(CpuModel.INTEL_80286)]
    public CpuModel CpuModel { get; init; }

    /// <summary>
    /// Gets if the A20 gate is silenced. If <c>true</c> memory addresses will rollover above 1 MB.
    /// </summary>
    [CommandOption("--A20Gate")]
    public bool A20Gate { get; init; }

    /// <summary>
    /// Gets if the program will be paused on start and stop. If <see cref="GdbPort"/> is set, the program will be paused anyway.
    /// </summary>
    [CommandOption("--Debug")]
    public bool Debug { get; init; }

    /// <summary> Path to C drive, default is exe parent. </summary>
    [CommandOption("-c|--CDrive <CDRIVE>")]
    public string? CDrive { get; set; }

    /// <summary> Path to executable. </summary>
    [CommandOption("-e|--Exe <EXE>")]
    public string Exe { get; set; } = string.Empty;

    /// <summary> List of parameters to give to the emulated program. </summary>
    [CommandOption("-a|--ExeArgs <EXEARGS>")]
    public string? ExeArgs { get; set; }

    /// <summary> Hexadecimal string representing the expected SHA256 checksum of the emulated program. </summary>
    [CommandOption("-x|--ExpectedChecksum <EXPECTEDCHECKSUM>")]
    public string? ExpectedChecksum { get; init; }

    /// <summary> The value of the expected SHA256 checksum. </summary>
    public byte[] ExpectedChecksumValue { get; set; } = Array.Empty<byte>();

    /// <summary> Instantiated <see cref="OverrideSupplierClassName"/>. Created by <see cref="CommandLineParser"/>. </summary>
    public IOverrideSupplier? OverrideSupplier { get; set; }

    /// <summary> Name of a class in the current folder that will generate the initial function information. See documentation for more information. </summary>
    [CommandOption("-o|--OverrideSupplierClassName <OVERRIDESUPPLIERCLASSNAME>")]
    public string? OverrideSupplierClassName { get; init; }

    /// <summary> If false it will use the names provided by <see cref="OverrideSupplierClassName"/> but not the code. Use this in the code for <see cref="UseCodeOverride"/>. </summary>
    [CommandOption("-u|--UseCodeOverride <USECODEOVERRIDE>")]
    public bool? UseCodeOverride { get; set; }

    /// <summary> Gets a value indicating whether the <see cref="UseCodeOverride"/> option is set to true. </summary>
    public bool UseCodeOverrideOption => UseCodeOverride ?? true;

    /// <summary>
    /// Flag indicating if headless mode is enabled. When this option is not specified, the normal UI is used.
    /// </summary>
    [CommandOption("-h|--HeadlessMode <HEADLESSMODE>")]
    public HeadlessType? HeadlessMode { get; init; }

    /// <summary>
    /// If true, will fail when encountering an unhandled IO port. Useful to check for unimplemented hardware. false by default.
    /// </summary>
    [CommandOption("-f|--FailOnUnhandledPort")]
    public bool FailOnUnhandledPort { get; init; }

    /// <summary>
    /// GDB port spice86 will listen to.
    /// </summary>
    [CommandOption("-g|--GdbPort <GDBPORT>")]
    [DefaultValue(10000)]
    public int GdbPort { get; init; }

    /// <summary>
    /// HTTP API port Spice86 will listen on.
    /// </summary>
    [CommandOption("--HttpApiPort|--http-api-port <HTTPAPIPORT>")]
    [DefaultValue(20000)]
    public int HttpApiPort { get; init; } = 20000;

    /// <summary>
    /// Directory to dump data to when not specified otherwise. If blank dumps to SPICE86_DUMPS_FOLDER, and if not defined dumps to a sub directory named with the program SHA 256 signature.
    /// </summary>
    [CommandOption("-r|--RecordedDataDirectory <RECORDEDDATADIRECTORY>")]
    public string? RecordedDataDirectory { get; init; }

    /// <summary>
    /// Install DOS interrupt vectors or not.
    /// </summary>
    [CommandOption("-v|--InitializeDOS <INITIALIZEDOS>")]
    public bool? InitializeDOS { get; set; }

    /// <summary>
    /// Only for <see cref="PitTimer"/>
    /// </summary>
    [CommandOption("-i|--InstructionTimeScale <INSTRUCTIONTIMESCALE>")]
    public long? InstructionTimeScale { get; set; }

    /// <summary>
    /// Optional seed for deterministic clock jitter. When set, both the cycles-driven clock and the
    /// real-time stopwatch clock add a small, reproducible signed offset (bounded to ±0.01 ms) to
    /// <c>ElapsedTimeMs</c>. When <c>null</c> (the default), clocks behave exactly as before.
    /// </summary>
    [CommandOption("--ClockJitterSeed <CLOCKJITTERSEED>")]
    public int? ClockJitterSeed { get; init; }

    /// <summary>
    /// Optional fixed start date/time for the emulated clock. Accepts any value parseable by
    /// <see cref="DateTimeOffset.Parse(string)"/> (e.g. an ISO 8601 string like
    /// <c>1993-06-01T00:00:00Z</c>). When <c>null</c> (the default), the emulated clock starts
    /// from <see cref="DateTimeOffset.UtcNow"/> at launch time.
    /// </summary>
    [CommandOption("--ClockStartTime <CLOCKSTARTTIME>")]
    public DateTimeOffset? ClockStartTime { get; init; }

    /// <summary>
    /// The time multiplier used for speeding up or slowing down the execution of the program.
    /// </summary>
    [CommandOption("-t|--TimeMultiplier <TIMEMULTIPLIER>")]
    [DefaultValue(1D)]
    public double TimeMultiplier { get; init; }

    /// <summary>
    /// The memory segment where the program will be loaded. The DOS PSP (Program Segment Prefix) and MCB (Memory Control Block) will be created before it.
    /// </summary>
    [CommandOption("-p|--ProgramEntryPointSegment <PROGRAMENTRYPOINTSEGMENT>")]
    [DefaultValue("0x170")]
    public string? ProgramEntryPointSegmentString { get => null; set => ProgramEntryPointSegment = CommandLineParser.ParseHexDecBinUInt16(value ?? throw new ArgumentNullException(nameof(value))); }
    public ushort ProgramEntryPointSegment;

    /// <summary>
    /// The memory address where the ASM handlers for interrupts and so on are to be written. Default is F000 which is the bios segment. Not all games will be happy with this changed to something else.
    /// </summary>
    [CommandOption("--ProvidedAsmHandlersSegment <PROVIDEDASMHANDLERSSEGMENT>")]
    [DefaultValue((ushort)0xF000)]
    public ushort ProvidedAsmHandlersSegment { get; init; }

    /// <summary>
    /// Determines whether logs should be silenced or not.
    /// </summary>
    [CommandOption("-s|--SilencedLogs")]
    public bool SilencedLogs { get; init; }

    /// <summary>
    /// Determines whether verbose level logs should be enabled or not.
    /// </summary>
    [CommandOption("-l|--VerboseLogs")]
    public bool VerboseLogs { get; init; }

    /// <summary>
    /// Determines whether warning level logs should be enabled or not.
    /// </summary>
    [CommandOption("-w|--WarningLogs")]
    public bool WarningLogs { get; init; }

    /// <summary>
    /// The path to the zip file or directory containing the MT-32 ROM files.
    /// </summary>
    [CommandOption("-m|--Mt32RomsPath <MT32ROMSPATH>")]
    public string? Mt32RomsPath { get; init; }

    /// <summary>
    /// Determines whether EMS (Expanded Memory Specification) should be enabled or not.
    /// </summary>
    [CommandOption("--Ems <EMS>")]
    public bool? Ems { get; init; }

    /// <summary>
    /// Specify the type of mouse to use.
    /// </summary>
    [CommandOption("--Mouse <MOUSE>")]
    [DefaultValue(MouseType.Ps2)]
    public MouseType Mouse { get; init; }

    /// <summary>
    /// Specify a C header file to be used for structure information
    /// </summary>
    [CommandOption("--StructureFile <STRUCTUREFILE>")]
    public string? StructureFile { get; init; }

    /// <summary>
    /// Audio engine to use
    /// </summary>
    [CommandOption("--AudioEngine <AUDIOENGINE>")]
    [DefaultValue(AudioEngine.CrossPlatform)]
    public AudioEngine AudioEngine { get; init; }

    /// <summary>
    /// Select the OPL synthesis mode.
    /// </summary>
    [CommandOption("--OplMode <OPLMODE>")]
    [DefaultValue(OplMode.Opl3)]
    public OplMode OplMode { get; init; }

    /// <summary>
    /// Sound Blaster type to emulate.
    /// </summary>
    [CommandOption("--SbType <SBTYPE>")]
    [DefaultValue(SbType.SBPro2)]
    public SbType SbType { get; init; }

    /// <summary>
    /// Sound Blaster base I/O address.
    /// </summary>
    [CommandOption("--SbBase <SBBASE>")]
    [DefaultValue((ushort)0x220)]
    public ushort SbBase { get; init; }

    /// <summary>
    /// Sound Blaster IRQ line.
    /// </summary>
    [CommandOption("--SbIrq <SBIRQ>")]
    [DefaultValue((byte)7)]
    public byte SbIrq { get; init; }

    /// <summary>
    /// Sound Blaster 8-bit DMA channel.
    /// </summary>
    [CommandOption("--SbDma <SBDMA>")]
    [DefaultValue((byte)1)]
    public byte SbDma { get; init; }

    /// <summary>
    /// Sound Blaster 16-bit high DMA channel.
    /// </summary>
    [CommandOption("--SbHdma <SBHDMA>")]
    [DefaultValue((byte)5)]
    public byte SbHdma { get; init; }

    /// <summary>
    /// Enable Sound Blaster mixer control of OPL voices.
    /// </summary>
    [CommandOption("--SbMixer <SBMIXER>")]
    [DefaultValue(true)]
    public bool? SbMixer { get; init; }
    [CommandOption("--Xms <XMS>")]
    public bool? Xms { get; init; }

    /// <summary>
    /// If true, will throw an exception and crash when encountering an invalid opcode.
    /// If false, will handle invalid opcodes as CPU faults (int 0x06).
    /// Default is true because usually invalid opcode means emulator bug.
    /// </summary>
    [CommandOption("--FailOnInvalidOpcode <FAILONINVALIDOPCODE>")]
    [DefaultValue(true)]
    public bool FailOnInvalidOpcode { get; init; }

    /// <summary>
    /// If true, logs every executed instruction to a file (similar to DOSBox heavy logging).
    /// This will significantly impact performance. Default is false.
    /// </summary>
    [CommandOption("--CpuHeavyLog")]
    public bool CpuHeavyLog { get; set; }

    /// <summary>
    /// Custom file path for CPU heavy log output. If not specified, defaults to {DumpDirectory}/cpu_heavy.log
    /// </summary>
    [CommandOption("--CpuHeavyLogDumpFile <CPUHEAVYLOGDUMPFILE>")]
    public string? CpuHeavyLogDumpFile { get; init; }

    /// <summary>
    /// Named expressions appended to every CPU heavy log line.
    /// Each entry must be in "name=expression" format (e.g. "life=AX+1" or
    /// "mem=word ptr ds:[0x0100]"). Evaluated per instruction; compiled once at startup.
    /// Only active when <see cref="CpuHeavyLog"/> is true.
    ///
    /// The option is named <c>--CpuHeavyLogExpressions</c> and accepts multiple values.
    /// Provide each pair as a quoted value when the expression contains spaces.
    /// Example: <c>--CpuHeavyLogExpressions "life=AX+1" "myvalue=word ptr ds:[0x0456]"</c>
    ///
    /// Splitting rule: the string is split on the first '='. For example
    /// <c>flag=AX==0</c> becomes name <c>flag</c> and expression <c>AX==0</c>.
    /// </summary>
    [CommandOption("--CpuHeavyLogExpressions <CPUHEAVYLOGEXPRESSIONS>")]
    public string[]? CpuHeavyLogExpressions { get; init; }

    [CommandOption("--AsmRenderingStyle <ASMRENDERINGSTYLE>")]
    [DefaultValue(AsmRenderingStyle.Spice86)]
    public AsmRenderingStyle AsmRenderingStyle { get; init; }

    /// <summary>
    /// The number of CPU cycles after which the emulator will automatically stop. When greater than 0, a cycle
    /// breakpoint is registered before the emulation starts and triggers a clean shutdown. Set to 0 (default)
    /// to leave the emulator running until explicitly stopped.
    /// </summary>
    [CommandOption("--StopAfterCycles <STOPAFTERCYCLES>")]
    public long StopAfterCycles { get; init; }

    /// <summary>
    /// Port for the MCP HTTP server.
    /// </summary>
    [CommandOption("--McpHttpPort <MCPHTTPPORT>")]
    [DefaultValue(8081)]
    public int McpHttpPort { get; init; }

    /// <summary>
    /// Selects the VGA rendering mode. Sync fires VGA events on the emulation thread for determinism;
    /// Async fires them on the UI thread for better performance.
    /// </summary>
    [CommandOption("--RenderingMode <RENDERINGMODE>")]
    [DefaultValue(RenderingMode.Async)]
    public RenderingMode RenderingMode { get; init; }

}
