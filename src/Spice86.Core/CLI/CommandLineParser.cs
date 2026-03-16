namespace Spice86.Core.CLI;

using CommandLine;

using Spice86.Core.Emulator.Function;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Utils;

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

/// <summary>
/// Parses the command line options to create a <see cref="Configuration"/>.
/// </summary>
public class CommandLineParser {
    /// <summary>
    /// Parses the command line into a <see cref="Configuration"/> object.
    /// </summary>
    /// <param name="args">The application command line arguments</param>
    /// <returns>A <see cref="Configuration"/> object representing the command line arguments</returns>
    /// <exception cref="UnreachableException">When the command line arguments are unrecognized.</exception>
    public Configuration? ParseCommandLine(string[] args) {
        string[] reducedArgs = ProcessArgs(args, out string exeArgs);

        ParserResult<Configuration?> result = Parser.Default.ParseArguments<Configuration?>(reducedArgs);
        return result.MapResult(initialConfig => {
            if (initialConfig is null) {
                return null;
            }
            initialConfig.Exe = ParseExePath(initialConfig.Exe);
            initialConfig.CDrive ??= Path.GetDirectoryName(initialConfig.Exe);
            initialConfig.ExpectedChecksumValue = string.IsNullOrWhiteSpace(initialConfig.ExpectedChecksum) ? Array.Empty<byte>() : ConvertUtils.HexToByteArray(initialConfig.ExpectedChecksum);
            initialConfig.OverrideSupplier = ParseFunctionInformationSupplierClassName(initialConfig.OverrideSupplierClassName);
            initialConfig.ExeArgs = exeArgs;
            if (initialConfig.Cycles != null) {
                initialConfig.InstructionsPerSecond = null;
            }
            if (initialConfig.CpuHeavyLogDumpFile != null) {
                initialConfig.CpuHeavyLog = true;
            }
            return initialConfig;
        }, error => null);
    }

    private static string[] ProcessArgs(string[] args, out string exeArgs) {
        var processedArgs = new List<string>();
        exeArgs = string.Empty;
        for (int i = 0; i < args.Length; i++) {
            if (IsExeArg(args[i]) && i + 1 <= args.Length) {
                exeArgs = args[i + 1];
                i++;
            } else {
                processedArgs.Add(args[i]);
            }
        }
        return processedArgs.ToArray();
    }

    private static bool IsExeArg(string arg) {
        (string ShortName, string LongName)? exeArgNames = GetCommandLineOptionName<Configuration>(nameof(Configuration.ExeArgs));
        return string.Equals(exeArgNames.Value.ShortName, arg, StringComparison.Ordinal) ||
               string.Equals(exeArgNames.Value.LongName, arg, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the command line option name for a given property name via the <see cref="OptionAttribute"/>
    /// </summary>
    private static (string ShortName, string LongName) GetCommandLineOptionName<T>(string propertyName) {
        PropertyInfo? property = typeof(T).GetProperty(propertyName);
        if (property?.GetCustomAttributes(typeof(OptionAttribute), false) is not OptionAttribute[] attribute || attribute.Length == 0) {
            throw new ArgumentException("Invalid propertyName", nameof(propertyName));
        }
        return ($"-{attribute[0].ShortName}", $"--{attribute[0].LongName}");
    }

    private static string ParseExePath(string exePath) {
        string unixPathValue = exePath.Replace('\\', '/');
        return File.Exists(exePath) ? new FileInfo(exePath).FullName : unixPathValue;
    }

    public static IOverrideSupplier? ParseFunctionInformationSupplierClassName(string? supplierClassName) {
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

    public static long ParseHexDecBinInt64(string input) {
        if (input is null)
            throw new ArgumentNullException(nameof(input));

        // Local regex: validates overall shape, no whitespace allowed
        Regex numberRegex = new Regex(
            @"^(0[xX][0-9a-fA-F]+|0[bB][01]+|[-+]?[0-9]+)$",
            RegexOptions.CultureInvariant);

        if (!numberRegex.IsMatch(input))
            throw new FormatException("Invalid number format.");

        int pos = 0;

        // Optional sign
        if (input[0] == '+' || input[0] == '-') {
            pos = 1;
        }

        ReadOnlySpan<char> s = input.AsSpan(pos);

        // Hexadecimal: 0x...
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            return Convert.ToInt64(s.ToString(), 16);
        }

        // Binary: 0b...
        if (s.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) {
            return Convert.ToInt64(input[2..], 2);
        }

        // Decimal
        return Convert.ToInt64(input, 10);
    }

    public static ushort ParseHexDecBinUInt16(string input) {
        long value = ParseHexDecBinInt64(input);

        if ((value < UInt16.MinValue) || (value > UInt16.MaxValue)) {
            throw new FormatException("value does not fit in UInt16");
        }

        return (ushort)value;
    }

}