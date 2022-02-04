namespace Spice86.CLI;

using CommandLine;

using Serilog;

using Spice86.Emulator;
using Spice86.Emulator.Errors;
using Spice86.Emulator.Function;
using Spice86.Utils;

using System;
using System.IO;
using System.Reflection;

/// <summary>
/// Parses the command line options to create a Configuration.<br/>
/// Displays help when configuration could not be parsed.
/// </summary>
public class CommandLineParser {
    private static readonly ILogger _logger = Log.Logger.ForContext<CommandLineParser>();

    public Configuration? ParseCommandLine(string[] args) {
        ParserResult<Configuration>? result = Parser.Default.ParseArguments<Configuration>(args)
            .WithNotParsed((e) => _logger.Error("{@Errors}",e));
        if (result != null) {
            Configuration? parsedConfig = result.MapResult((initialConfig) => {
                initialConfig.Exe = ParseExePath(initialConfig.Exe);
                initialConfig.ExpectedChecksumValue = string.IsNullOrWhiteSpace(initialConfig.ExpectedChecksum) ? Array.Empty<byte>() : ConvertUtils.HexToByteArray(initialConfig.ExpectedChecksum);
                initialConfig.OverrideSupplier = ParseFunctionInformationSupplierClassName(initialConfig.OverrideSupplierClass);
                return initialConfig;
            }, (error) => {
                return null!;
            });
            return parsedConfig;
        }
        return null;
    }

    private static string? ParseExePath(string? exePath) {
        string? unixPathValue = exePath?.Replace('\\', '/');
        if(File.Exists(exePath)) {
            return new FileInfo(exePath).FullName;
        }
        return unixPathValue;
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