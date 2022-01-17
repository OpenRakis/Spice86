namespace Spice86.UI;

using Serilog;

using System;
using System.Collections.Generic;
using System.Linq;


using Spice86.Emulator.Errors;
using Spice86.Emulator.Function;
using Spice86.Utils;
using Spice86.Emulator;
using System.Reflection;

/// <summary>
/// Parses the command line options to create a Configuration.<br/>
/// Displays help when configuration could not be parsed.
/// </summary>
public class CommandLineParser {
    private static readonly ILogger _logger = Log.Logger.ForContext<CommandLineParser>();
    private static readonly int DefaultProgramStartSegment = 0x01ED;

    private string? GetCDrive(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        string unixPathValue = value.Replace('\\', '/');
        if (!unixPathValue.EndsWith("/")) {
            unixPathValue += "/";
        }

        return unixPathValue;
    }

    private string? GetExe(string[] args) {
        if(args.Any() == false) {
            return null;
        }
        return args.FirstOrDefault();
    }

    private string ParseDefaultDumpDirectory(string defaultDumpDirectory) {
        if (string.IsNullOrWhiteSpace(defaultDumpDirectory)) {
            return Environment.CurrentDirectory;
        }

        return defaultDumpDirectory;
    }

    private byte[] ParseExpectedChecksum(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return new byte[0];
        }

        return ConvertUtils.HexToByteArray(value);
    }

    private bool? ParseFailOnUnhandledPort(string value) {
        if (bool.TryParse(value, out bool result)) {
            return result == true;
        }
        return false;
    }

    public Configuration? ParseCommandLine(string[] args) {
        var configuration = new Configuration();
        configuration.SetExe(GetExe(args));
        if(string.IsNullOrWhiteSpace(configuration.GetExe())) {
            _logger.Information(@"Parameters:
              <path to exe>
              --exeArgs=""<List of parameters to give to the emulated program>""
              --cDrive=<path to C drive, default is .>
              --instructionsPerSecond=<number of instructions that have to be executed executed by the emulator to consider a second passed> if blank will use time based timer.
              --timeMultiplier=<time multiplier> if >1 will go faster, if <1 will go slower.
              --gdbPort=<gdb port, if empty gdb server will not be created. If not empty, application will pause until gdb connects>
              --overrideSupplierClassName=<Name of a class in the classpath that will generate the initial function informations. See documentation for more information.>
              --useCodeOverride=<true or false> if false it will use the names provided by overrideSupplierClassName but not the code
              --programEntryPointSegment=<Segment where to load the program. DOS PSP and MCB will be created before it>
              --expectedChecksum=<Hexadecimal string representing the expected checksum of the checksum>
              --failOnUnhandledPort=<if true, will fail when encountering an unhandled IO port. Useful to check for unimplemented hardware. false by default.>
              --defaultDumpDirectory=<Directory to dump data to when not specified otherwise. Workin directory if blank>");
            return null;
        }
        //TODO: complete it. Maybe use System.CommandLine nuget package...
        return configuration;
    }

    private IOverrideSupplier? ParseFunctionInformationSupplierClassName(string supplierClassName) {
        if (supplierClassName == null) {
            return null;
        }

        try {
            Type? supplierClass = Type.GetType(supplierClassName);
            if (!typeof(IOverrideSupplier).IsAssignableFrom(supplierClass)) {
                string error = $"Provided class {supplierClassName} does not implement the {typeof(IOverrideSupplier).FullName} interface ";
                throw new UnrecoverableException(error);
            }

            return (IOverrideSupplier?)Activator.CreateInstance(supplierClass);
        } catch (MethodAccessException exception) {
            throw new UnrecoverableException("Could not load provided class " + supplierClassName, exception);
        } catch (TargetInvocationException exception) {
            throw new UnrecoverableException("Could not instantiate provided class " + supplierClassName, exception);
        } catch (NotSupportedException exception) {
            throw new UnrecoverableException("Could not instantiate provided class " + supplierClassName, exception);
        } catch (ArgumentException exception) {
            throw new UnrecoverableException("Could not instantiate provided class " + supplierClassName, exception);
        } catch (MemberAccessException exception) {
            throw new UnrecoverableException("Could not instantiate provided class " + supplierClassName, exception);
        } catch (TypeLoadException exception) {
            throw new UnrecoverableException("Could not instantiate provided class " + supplierClassName, exception);
        }
    }

    private long? ParseInstructionsPerSecondParameter(string value) {
        if (long.TryParse(value, out var longValue)) {
            return longValue;
        }

        return null;
    }

    private int? ParseInt(string value) {
        if (int.TryParse(value, out var intValue)) {
            return intValue;
        }

        return null;
    }

    private int? ParseProgramEntryPointSegment(string value) {
        int? segment = ParseInt(value);
        if (segment == null) {
            return DefaultProgramStartSegment;
        }

        return segment;
    }

    private double ParseTimeMultiplier(string value) {
        if (!double.TryParse(value, out var doubleValue)) {
            return 1;
        }
        return doubleValue;
    }

    private bool ParseUseFunctionOverride(string value) {
        if (bool.TryParse(value, out var boolValue)) {
            return boolValue != false;
        }
        return true;
    }
}