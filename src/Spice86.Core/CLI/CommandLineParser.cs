namespace Spice86.Core.CLI;

using CommandLine;

using Spice86.Core.Emulator.Function;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Utils;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;


/// <inheritdoc cref="ICommandLineParser" />
public class CommandLineParser : ICommandLineParser {
    /// <inheritdoc />
    public Configuration ParseCommandLine(string[] args) {
        string[] reducedArgs = ProcessArgs(args, out string exeArgs);

        ParserResult<Configuration> result = Parser.Default.ParseArguments<Configuration>(reducedArgs);
        return result.MapResult(initialConfig => {
            initialConfig.Exe = ParseExePath(initialConfig.Exe);
            initialConfig.CDrive ??= Path.GetDirectoryName(initialConfig.Exe);
            initialConfig.ExpectedChecksumValue = string.IsNullOrWhiteSpace(initialConfig.ExpectedChecksum) ? Array.Empty<byte>() : ConvertUtils.HexToByteArray(initialConfig.ExpectedChecksum);
            initialConfig.OverrideSupplier = ParseFunctionInformationSupplierClassName(initialConfig);
            initialConfig.ExeArgs = exeArgs;
            return initialConfig;
        }, error => {
            string? message = "Unparseable command line";
            var exception = new UnreachableException(message);
            exception.Data.Add("Error", error);
            Environment.FailFast(message, exception);
            throw exception;
        });
    }
    
    private static string[] ProcessArgs(string[] args, out string exeArgs) {
        var processedArgs = new List<string>();
        exeArgs = string.Empty;
        for (int i = 0; i < args.Length; i++) {
            if (IsExeArg(args[i]) && i + 1 <= args.Length) {
                exeArgs = args[i + 1];
                i++;
            } else  {
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

    private static string? ParseExePath(string? exePath) {
        string? unixPathValue = exePath?.Replace('\\', '/');
        return File.Exists(exePath) ? new FileInfo(exePath).FullName : unixPathValue;
    }

    private static IOverrideSupplier? ParseFunctionInformationSupplierClassName(Configuration configuration) {
        string? supplierClassName = configuration.OverrideSupplierClassName;
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
}