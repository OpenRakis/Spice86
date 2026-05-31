namespace Spice86.Core.Emulator;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.LoadableFile.Bios;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.IO;
using System.Security.Cryptography;

/// <summary>
/// Loads the initial program (COM, EXE, BAT, or BIOS) into emulator memory and validates the file
/// against the optional SHA-256 checksum configured for the run.
/// </summary>
internal sealed class ProgramBootstrapper {
    private readonly Configuration _configuration;
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly DosInt21Handler _int21Handler;
    private readonly ILoggerService _loggerService;

    public ProgramBootstrapper(
        Configuration configuration,
        IMemory memory,
        State state,
        DosInt21Handler int21Handler,
        ILoggerService loggerService) {
        _configuration = configuration;
        _memory = memory;
        _state = state;
        _int21Handler = int21Handler;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Selects the loader for the configured executable, loads it into memory, and validates its checksum.
    /// </summary>
    public void LoadInitialProgram() {
        string? executableFileName = _configuration.Exe;
        ArgumentException.ThrowIfNullOrEmpty(executableFileName);

        string upperCaseExtension = Path.GetExtension(executableFileName.ToUpperInvariant());
        bool isDosProgram = upperCaseExtension is ".EXE" or ".COM" or ".BAT";

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Preparing initial load for {FileName} (DOS program: {IsDosProgram})",
                executableFileName, isDosProgram);
        }

        ExecutableFileLoader loader = CreateLoader(executableFileName, isDosProgram, upperCaseExtension);

        try {
            if (_configuration.InitializeDOS is null) {
                _configuration.InitializeDOS = loader.DosInitializationNeeded;
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("InitializeDOS parameter not provided. Guessed value is: {InitializeDOS}",
                        _configuration.InitializeDOS);
                }
            }
            byte[] fileContent = loader.LoadFile(executableFileName, _configuration.ExeArgs);
            CheckSha256Checksum(fileContent, _configuration.ExpectedChecksumValue);
        } catch (IOException e) {
            throw new UnrecoverableException($"Failed to read file {executableFileName}", e);
        }
    }

    private ExecutableFileLoader CreateLoader(string executableFileName, bool isDosProgram, string upperCaseExtension) {
        if (!isDosProgram) {
            return new BiosLoader(_memory, _state, _loggerService);
        }

        if (upperCaseExtension == ".BAT") {
            return new DosBatchProgramLoader(_configuration, _memory, _state, _int21Handler, _loggerService);
        }

        return new DosProgramLoader(_configuration, _memory, _state, _int21Handler, _loggerService);
    }

    private static void CheckSha256Checksum(byte[] file, byte[]? expectedHash) {
        ArgumentNullException.ThrowIfNull(expectedHash, nameof(expectedHash));
        if (expectedHash.Length == 0) {
            return;
        }

        byte[] actualHash = SHA256.HashData(file);

        if (!actualHash.AsSpan().SequenceEqual(expectedHash)) {
            string error =
                $"File does not match the expected SHA256 checksum, cannot execute it.\nExpected checksum is {ConvertUtils.ByteArrayToHexString(expectedHash)}.\nGot {ConvertUtils.ByteArrayToHexString(actualHash)}\n";
            throw new UnrecoverableException(error);
        }
    }
}
