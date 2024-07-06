namespace Spice86.Core.CLI;

using CommandLine;

using Spice86.Core.Emulator.Function;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Utils;

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;


/// <inheritdoc cref="ICommandLineParser" />
public class CommandLineParser : ICommandLineParser {
    /// <inheritdoc />
    public Configuration ParseCommandLine(string[] args) {
        ParserResult<Configuration> result = Parser.Default.ParseArguments<Configuration>(args);
        return result.MapResult(initialConfig => {
            initialConfig.Exe = ParseExePath(initialConfig.Exe);
            initialConfig.CDrive ??= Path.GetDirectoryName(initialConfig.Exe);
            initialConfig.ExpectedChecksumValue = string.IsNullOrWhiteSpace(initialConfig.ExpectedChecksum) ? Array.Empty<byte>() : ConvertUtils.HexToByteArray(initialConfig.ExpectedChecksum);
            initialConfig.OverrideSupplier = ParseFunctionInformationSupplierClassName(initialConfig);
            return initialConfig;
        }, error => {
            string? message = "Unparseable command line";
            var exception = new UnreachableException(message);
            exception.Data.Add("Error", error);
            Environment.FailFast(message, exception);
            throw exception;
        });
    }

    private static string? ParseExePath(string? exePath) {
        string? unixPathValue = exePath?.Replace('\\', '/');
        if (File.Exists(exePath)) {
            return new FileInfo(exePath).FullName;
        }
        return unixPathValue;
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