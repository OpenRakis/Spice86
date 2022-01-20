namespace Spice86.CLI;

using Serilog;

using Spice86.Emulator;
using Spice86.Emulator.Errors;
using Spice86.Emulator.Function;
using Spice86.Utils;

using System;
using System.CommandLine;
using System.Linq;
using System.Reflection;

/// <summary>
/// Parses the command line options to create a Configuration.<br/>
/// Displays help when configuration could not be parsed.
/// </summary>
public class CommandLineParser {
    private static readonly ILogger _logger = Log.Logger.ForContext<CommandLineParser>();
    private const int DefaultProgramStartSegment = 0x01ED;

    public Configuration? ParseCommandLine(string[] args) {
        if(args.Any() == false) {
            _logger.Information($@"Parameters:
              --exe=path to executable
              --exeArgs=""<List of parameters to give to the emulated program>""
              --cDrive=<path to C drive, default is .>
              --instructionsPerSecond=<number of instructions that have to be executed executed by the emulator to consider a second passed> if blank will use time based timer.
              --timeMultiplier=<time multiplier> if >1 will go faster, if <1 will go slower.
              --gdbPort=<gdb port, if empty gdb server will not be created. If not empty, application will pause until gdb connects>
              --overrideSupplierClassName=<Name of a class in the current folder that will generate the initial function informations. See documentation for more information.>
              --useCodeOverride=<true or false> if false it will use the names provided by overrideSupplierClassName but not the code
              --programEntryPointSegment=<Segment where to load the program. DOS PSP and MCB will be created before it>
              --expectedChecksum=<Hexadecimal string representing the expected checksum of the checksum>
              --failOnUnhandledPort=<if true, will fail when encountering an unhandled IO port. Useful to check for unimplemented hardware. false by default.>
              --defaultDumpDirectory=<Directory to dump data to when not specified otherwise. Workin directory if blank>");
            return null;
        }

        var exePath = new Option<string?>(
            "--exe-path",
            "path to executable"
            );

        var exeArgs = new Option<string?>(
            "--exeArgs",
            "List of parameters to give to the emulated program"
            );

        var cDrive = new Option<string?>(
            "--cDrive",
            () => Environment.CurrentDirectory,
            "path to C drive, default is ."
            );

        var instructionsPerSecond = new Option<long>(
            "--instructionsPerSecond",
            "number of instructions that have to be executed executed by the emulator to consider a second passed> if blank will use time based timer"
            );

        var timeMultiplier = new Option<double>(
            "--timeMultiplier",
            "<time multiplier> if >1 will go faster, if <1 will go slower."
            );

        var gdbPort = new Option<int?>(
            "--gdbPort",
            "<gdb port, if empty gdb server will not be created. If not empty, application will pause until gdb connects>");

        var overrideSupplierClassName = new Option<string?>(
            "--overrideSupplierClassName",
            "<Name of a class in the current folder that will generate the initial function informations. See documentation for more information.>"
            );

        var useCodeOverride = new Option<bool>(
            "--useCodeOverride",
            () => true,
            "<true or false> if false it will use the names provided by overrideSupplierClassName but not the code"
            );

        var programEntryPointSegment = new Option<int>(
            "--programEntryPointSegment",
            "<Segment where to load the program. DOS PSP and MCB will be created before it>"
            );

        var expectedChecksum = new Option<string?>(
            "--expectedChecksum",
            "<Hexadecimal string representing the expected checksum of the checksum>"
            );

        var failOnUnhandledPort = new Option<bool>(
            "--failOnUnhandledPort",
            () => false,
            "<if true, will fail when encountering an unhandled IO port. Useful to check for unimplemented hardware. false by default.>"
            );

        var defaultDumpDirectory = new Option<string?>(
            "--defaultDumpDirectory",
            "<Directory to dump data to when not specified otherwise. Workin directory if blank>"
            );

        var rootCommand = new RootCommand {
            exePath,
            exeArgs,
            cDrive,
            instructionsPerSecond,
            timeMultiplier,
            gdbPort,
            overrideSupplierClassName,
            useCodeOverride,
            programEntryPointSegment,
            expectedChecksum,
            failOnUnhandledPort,
            defaultDumpDirectory
        };

        Configuration? configuration = null;

        rootCommand.Description = nameof(Spice86);

        rootCommand.SetHandler(
            (string exepath, string? exeArgs, string? cDrive, long instructionsPerSecond,
            double timeMultiplier, int? gdbPort, string? overrideSupplierClassName, bool useCodeOverride,
            int programEntryPointSegment, string? expectedChecksum, bool failOnUnhandledPort, string? defaultDumpDirectory) => {
                configuration =
                HandleParsedRootCommand(exepath, exeArgs, cDrive, instructionsPerSecond, timeMultiplier, gdbPort, overrideSupplierClassName, useCodeOverride, programEntryPointSegment,
                    expectedChecksum, failOnUnhandledPort, defaultDumpDirectory);
            }, exePath, exeArgs, cDrive, instructionsPerSecond, timeMultiplier, gdbPort, overrideSupplierClassName, useCodeOverride, programEntryPointSegment,
                    expectedChecksum, failOnUnhandledPort, defaultDumpDirectory
            );


        if(rootCommand.Invoke(args) == 0) {
            if(string.IsNullOrWhiteSpace(configuration?.Exe)) {
                return null;
            }
            return configuration;
        }


        return null;
    }

    private static Configuration HandleParsedRootCommand(string exePath, string? exeArgs, string? cDrive, long instructionsPerSecond,
        double timeMultiplier, int? gdbPort, string overrideSupplierClassName, bool useCodeOverride,
        int programEntryPointSegment, string? expectedChecksum, bool failOnUnhandledPort, string? defaultDumpDirectory) {
        var expectedChecksumBytes = string.IsNullOrWhiteSpace(expectedChecksum) ? Array.Empty<byte>() : ConvertUtils.HexToByteArray(expectedChecksum);
        string unixPathValue = exePath.Replace('\\', '/');
        if (!unixPathValue.EndsWith("/")) {
            unixPathValue += "/";
        }
        IOverrideSupplier? overrideSupplier = ParseFunctionInformationSupplierClassName(overrideSupplierClassName);
        return new Configuration() {
            Exe = unixPathValue,
            ExeArgs = exeArgs,
            CDrive = cDrive,
            InstructionsPerSecond = instructionsPerSecond,
            TimeMultiplier = timeMultiplier,
            DefaultDumpDirectory = defaultDumpDirectory,
            ExpectedChecksum = expectedChecksumBytes.ToArray(),
            FailOnUnhandledPort = failOnUnhandledPort,
            OverrideSupplier = overrideSupplier,
            GdbPort = gdbPort,
            InstallInterruptVector = true,
            UseCodeOverride = useCodeOverride,
            ProgramEntryPointSegment = programEntryPointSegment
        };

    }

    private static IOverrideSupplier? ParseFunctionInformationSupplierClassName(string? supplierClassName) {
        if (supplierClassName == null) {
            return null;
        }

        try {
            var supplierClass = Type.GetType(supplierClassName);
            if (!typeof(IOverrideSupplier).IsAssignableFrom(supplierClass)) {
                string error = $"Provided class {supplierClassName} does not implement the {typeof(IOverrideSupplier).FullName} interface ";
                throw new UnrecoverableException(error);
            }

            return (IOverrideSupplier?)Activator.CreateInstance(supplierClass);
        } catch (MethodAccessException exception) {
            throw new UnrecoverableException($"Could not load provided class {supplierClassName}", exception);
        } catch (TargetInvocationException exception) {
            throw new UnrecoverableException($"Could not instantiate provided class {supplierClassName}", exception);
        } catch (NotSupportedException exception) {
            throw new UnrecoverableException($"Could not instantiate provided class {supplierClassName}", exception);
        } catch (ArgumentException exception) {
            throw new UnrecoverableException($"Could not instantiate provided class {supplierClassName}", exception);
        } catch (MemberAccessException exception) {
            throw new UnrecoverableException($"Could not instantiate provided class {supplierClassName}", exception);
        } catch (TypeLoadException exception) {
            throw new UnrecoverableException($"Could not instantiate provided class {supplierClassName}", exception);
        }
    }
}