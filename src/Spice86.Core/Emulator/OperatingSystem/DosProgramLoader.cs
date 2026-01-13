namespace Spice86.Core.Emulator.OperatingSystem;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Serilog.Events;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Command.BatchProcessing;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

internal class DosProgramLoader : DosFileLoader {
    private readonly Configuration _configuration;
    private readonly DosInt21Handler _int21;
    private readonly DosProcessManager _processManager;
    
    public DosProgramLoader(Configuration configuration, IMemory memory,
        State state, DosInt21Handler int21Handler,
        ILoggerService loggerService)
        : base(memory, state, loggerService) {
        _configuration = configuration;
        _int21 = int21Handler;
        _processManager = int21Handler.ProcessManager;
    }

    public override byte[] LoadFile(string file, string? arguments) {
        _processManager.CreateRootCommandComPsp();

        string basePath = _configuration.CDrive ?? string.Empty;
        string hostPath = Path.IsPathRooted(file) || string.IsNullOrWhiteSpace(basePath)
            ? Path.GetFullPath(file)
            : Path.GetFullPath(Path.Combine(basePath, file));
        string dosPath = _processManager.FileManager.GetDosProgramPath(hostPath);

        string commandArguments = arguments ?? string.Empty;
        string extension = Path.GetExtension(hostPath).ToUpperInvariant();

        if (extension == ".BAT") {
            IBatchEnvironment environment = new DosEnvironmentAdapter(_processManager.GetEnvironmentVariable);
            BatchProcessor batchProcessor = new(_loggerService, environment);
            BatchExecutor executor = new(_processManager, _processManager.FileManager, batchProcessor, _loggerService);
            string[] argsArray = SplitArguments(commandArguments);
            bool launched = executor.ExecuteWithAutoexec(dosPath, argsArray);
            if (launched && executor.LaunchedExecutableContent is not null) {
                return executor.LaunchedExecutableContent;
            }
            return File.ReadAllBytes(hostPath);
        }

        DosExecParameterBlock paramBlock = new(new ByteArrayReaderWriter(new byte[DosExecParameterBlock.Size]), 0);
        _int21.LoadAndExecute(dosPath, paramBlock, commandTail: commandArguments);
        return File.ReadAllBytes(hostPath);
    }

    private static string[] SplitArguments(string arguments) {
        if (string.IsNullOrWhiteSpace(arguments)) {
            return Array.Empty<string>();
        }
        return arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class BatchExecutor {
        private readonly DosProcessManager _processManager;
        private readonly DosFileManager _fileManager;
        private readonly ILoggerService _loggerService;
        private readonly BatchProcessor _batchProcessor;

        public string? LaunchedExecutablePath { get; private set; }
        public byte[]? LaunchedExecutableContent { get; private set; }

        public BatchExecutor(DosProcessManager processManager, DosFileManager fileManager, BatchProcessor batchProcessor, ILoggerService loggerService) {
            _processManager = processManager;
            _fileManager = fileManager;
            _batchProcessor = batchProcessor;
            _loggerService = loggerService;
        }

        public bool ExecuteUntilProgram(string dosBatchPath, string[] arguments) {
            string? hostBatchPath = _fileManager.TryGetFullHostPathFromDos(dosBatchPath);
            if (string.IsNullOrWhiteSpace(hostBatchPath) || !File.Exists(hostBatchPath)) {
                return false;
            }

            if (!_batchProcessor.StartBatch(hostBatchPath, arguments)) {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("BatchExecutor: Failed to start batch file: {Path}", dosBatchPath);
                }
                return false;
            }

            return ProcessBatchLoop();
        }

        public bool ExecuteWithAutoexec(string dosBatchPath, string[] arguments) {
            string joinedArguments = string.Join(' ', arguments);
            AutoexecGenerator generator = AutoexecGenerator.ForBatch(dosBatchPath, joinedArguments);
            string[] autoexecLines = generator.Generate();
            StringArrayLineReader reader = new StringArrayLineReader(autoexecLines);
            if (!_batchProcessor.StartBatchWithReader("AUTOEXEC.BAT", Array.Empty<string>(), reader)) {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("BatchExecutor: Failed to start AUTOEXEC.BAT for: {Path}", dosBatchPath);
                }
                return false;
            }

            return ProcessBatchLoop();
        }

        private bool ProcessBatchLoop() {
            while (true) {
                string? line = _batchProcessor.ReadNextLine(out bool _);
                if (line is null) {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("BatchExecutor: Batch file ended without launching a program");
                    }
                    return false;
                }

                string batchDirDos = string.Empty;
                string? currentBatchHostPath = _batchProcessor.CurrentBatchPath;
                if (!string.IsNullOrEmpty(currentBatchHostPath)) {
                    string currentBatchDosPath = _fileManager.GetDosProgramPath(currentBatchHostPath);
                    batchDirDos = Path.GetDirectoryName(currentBatchDosPath) ?? string.Empty;
                }

                BatchCommand command = _batchProcessor.ParseCommand(line);

                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug(
                        "BatchExecutor: Processing command type={Type}, value='{Value}'",
                        command.Type, command.Value);
                }

                bool shouldContinue = ProcessCommand(command, batchDirDos);
                if (!shouldContinue) {
                    return LaunchedExecutablePath is not null;
                }
            }
        }

        private bool ProcessCommand(BatchCommand command, string batchDirDos) {
            switch (command.Type) {
                case BatchCommandType.Empty:
                    return true;

                case BatchCommandType.PrintMessage:
                    HandlePrintMessage(command);
                    return true;

                case BatchCommandType.SetVariable:
                    HandleSetVariable(command);
                    return true;

                case BatchCommandType.ShowEchoState:
                    HandleShowEchoState(command);
                    return true;

                case BatchCommandType.ShowVariables:
                    HandleShowVariables();
                    return true;

                case BatchCommandType.ShowVariable:
                    HandleShowVariable(command);
                    return true;

                case BatchCommandType.Shift:
                    return true;

                case BatchCommandType.Goto:
                    if (!string.IsNullOrEmpty(command.Value)) {
                        bool found = _batchProcessor.GotoLabel(command.Value);
                        if (!found && _loggerService.IsEnabled(LogEventLevel.Warning)) {
                            _loggerService.Warning("BatchExecutor: Label not found: {Label}", command.Value);
                        }
                    }
                    return true;

                case BatchCommandType.Exit:
                    if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                        _loggerService.Information("BatchExecutor: EXIT command encountered");
                    }
                    return false;

                case BatchCommandType.Pause:
                    HandlePause();
                    return true;

                case BatchCommandType.If:
                    return HandleIf(command, batchDirDos);

                case BatchCommandType.For:
                    return HandleFor(command, batchDirDos);

                case BatchCommandType.ExecuteProgram:
                case BatchCommandType.CallBatch:
                    return HandleExecuteProgram(command, batchDirDos);

                default:
                    return true;
            }
        }

        private bool HandleIf(BatchCommand command, string batchDirDos) {
            bool condition = EvaluateIfCondition(command, batchDirDos);
            if (command.Negate) {
                condition = !condition;
            }

            if (!condition) {
                return true;
            }

            string thenPart = ExtractIfThenPart(command);
            if (string.IsNullOrWhiteSpace(thenPart)) {
                return true;
            }

            BatchCommand nested = _batchProcessor.ParseCommand(thenPart);
            return ProcessCommand(nested, batchDirDos);
        }

        private bool EvaluateIfCondition(BatchCommand command, string batchDirDos) {
            switch (command.Value.ToUpperInvariant()) {
                case "EXIST":
                    {
                        string conditionArguments = command.Arguments.TrimStart();
                        string target = ReadFirstToken(conditionArguments);
                        if (string.IsNullOrEmpty(target)) {
                            return false;
                        }
                        string dosCandidate = target;
                        if (!target.Contains(':') && !string.IsNullOrEmpty(batchDirDos)) {
                            dosCandidate = batchDirDos.Length == 0 ? target : batchDirDos + "\\" + target;
                        }
                        string? hostPath = _fileManager.TryGetFullHostPathFromDos(dosCandidate);
                        if (string.IsNullOrEmpty(hostPath)) {
                            return false;
                        }
                        return File.Exists(hostPath);
                    }
                case "ERRORLEVEL":
                    {
                        string token = ReadFirstToken(command.Arguments);
                        int requiredLevel;
                        bool parsed = int.TryParse(token, out requiredLevel);
                        if (!parsed) {
                            return false;
                        }
                        int exitCode = _processManager.LastChildReturnCode & 0xFF;
                        return exitCode >= requiredLevel;
                    }
                case "COMPARE":
                    {
                        string arguments = command.Arguments;
                        int equalsIndex = arguments.IndexOf("==", StringComparison.Ordinal);
                        if (equalsIndex <= 0) {
                            return false;
                        }
                        string left = arguments[..equalsIndex];
                        string remainder = arguments[(equalsIndex + 2)..];
                        string right = ReadFirstToken(remainder);
                        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
                    }
                default:
                    return false;
            }
        }

        private static string ExtractIfThenPart(BatchCommand command) {
            switch (command.Value.ToUpperInvariant()) {
                case "EXIST":
                    {
                        string args = command.Arguments.TrimStart();
                        string target = ReadFirstToken(args);
                        if (string.IsNullOrEmpty(target)) {
                            return string.Empty;
                        }
                        string afterTarget = args[target.Length..].TrimStart();
                        return afterTarget;
                    }
                case "ERRORLEVEL":
                    {
                        string args = command.Arguments.TrimStart();
                        string token = ReadFirstToken(args);
                        if (string.IsNullOrEmpty(token)) {
                            return string.Empty;
                        }
                        string afterNumber = args[token.Length..].TrimStart();
                        return afterNumber;
                    }
                case "COMPARE":
                    {
                        string arguments = command.Arguments;
                        int equalsIndex = arguments.IndexOf("==", StringComparison.Ordinal);
                        if (equalsIndex <= 0) {
                            return string.Empty;
                        }
                        string remainder = arguments[(equalsIndex + 2)..];
                        string right = ReadFirstToken(remainder);
                        if (string.IsNullOrEmpty(right)) {
                            return string.Empty;
                        }
                        int startOfThen = equalsIndex + 2 + right.Length;
                        if (startOfThen >= arguments.Length) {
                            return string.Empty;
                        }
                        string thenPart = arguments[(startOfThen)..].TrimStart();
                        return thenPart;
                    }
                default:
                    return string.Empty;
            }
        }

        private bool HandleFor(BatchCommand command, string batchDirDos) {
            string variable = command.Value;
            string[] setItems = command.GetForSet();
            string template = command.GetForCommand();

            for (int i = 0; i < setItems.Length; i++) {
                string item = setItems[i];
                string replaced = template.Replace(variable, item, StringComparison.OrdinalIgnoreCase);
                BatchCommand nested = _batchProcessor.ParseCommand(replaced);
                bool shouldContinue = ProcessCommand(nested, batchDirDos);
                if (!shouldContinue) {
                    return false;
                }
            }

            return true;
        }

        private bool HandleExecuteProgram(BatchCommand command, string batchDirDos) {
            string programName = command.Value;
            string programArgs = command.Arguments;

            if (string.IsNullOrEmpty(programName)) {
                return true;
            }

            ProgramResolutionResult resolution = ResolveProgram(programName, batchDirDos);
            if (!resolution.Found) {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning(
                        "BatchExecutor: Could not find program '{Program}'",
                        programName);
                }
                return true;
            }

            string extension = Path.GetExtension(resolution.DosPath).ToUpperInvariant();
            if (extension == ".BAT") {
                string[] childArgs = SplitArguments(programArgs);
                _batchProcessor.StartBatch(resolution.HostPath, childArgs);
                return true;
            }

            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information(
                    "BatchExecutor: Launching program '{Program}' with args '{Args}'",
                    resolution.DosPath, programArgs);
            }

            LaunchedExecutablePath = resolution.HostPath;
            LaunchedExecutableContent = File.ReadAllBytes(resolution.HostPath);

            DosExecResult result = _processManager.LoadOrLoadAndExecute(
                resolution.DosPath,
                new DosExecParameterBlock(new ByteArrayReaderWriter(new byte[DosExecParameterBlock.Size]), 0),
                programArgs, DosExecLoadType.LoadAndExecute, 0);

            if (!result.Success) {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error(
                        "BatchExecutor: Failed to launch program '{Program}': {Error}",
                        programName, result.ErrorCode);
                }
                return true;
            }

            return false;
        }

        private ProgramResolutionResult ResolveProgram(string programName, string batchDirDos) {
            List<string> candidates = new List<string>();
            bool hasExtension = !string.IsNullOrEmpty(Path.GetExtension(programName));

            if (batchDirDos.Length > 0 && programName.IndexOf(':') < 0 && programName.IndexOf('\\') < 0 && programName.IndexOf('/') < 0) {
                candidates.Add(batchDirDos + "\\" + programName);
            }
            candidates.Add(programName);

            string[] extensions = new[] { ".COM", ".EXE", ".BAT" };

            for (int c = 0; c < candidates.Count; c++) {
                string candidate = candidates[c];
                if (hasExtension) {
                    ProgramResolutionResult? found = TryResolve(candidate);
                    if (found.HasValue && found.Value.Found) {
                        return found.Value;
                    }
                } else {
                    for (int i = 0; i < extensions.Length; i++) {
                        string withExt = candidate + extensions[i];
                        ProgramResolutionResult? found = TryResolve(withExt);
                        if (found.HasValue && found.Value.Found) {
                            return found.Value;
                        }
                    }
                }
            }

            return ProgramResolutionResult.NotFound;
        }

        private ProgramResolutionResult? TryResolve(string dosPathCandidate) {
            string? hostPath = _fileManager.TryGetFullHostPathFromDos(dosPathCandidate);
            if (string.IsNullOrEmpty(hostPath)) {
                return null;
            }
            if (!File.Exists(hostPath)) {
                return null;
            }
            string dosPath = _fileManager.GetDosProgramPath(hostPath);
            ProgramResolutionResult result = new ProgramResolutionResult(true, dosPath, hostPath);
            return result;
        }

        private static string ReadFirstToken(string value) {
            string trimmed = value.TrimStart();
            int spaceIndex = trimmed.IndexOfAny(new[] { ' ', '\t' });
            if (spaceIndex < 0) {
                return trimmed;
            }
            return trimmed[..spaceIndex];
        }

        private static string[] SplitArguments(string arguments) {
            if (string.IsNullOrWhiteSpace(arguments)) {
                return Array.Empty<string>();
            }
            return arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        private void HandlePrintMessage(BatchCommand command) {
            string message = command.Value;
            PrintToConsole(message);
            PrintToConsole("\r\n");
        }

        private void HandleShowEchoState(BatchCommand command) {
            string state = command.Value; // "ON" or "OFF"
            PrintToConsole($"ECHO is {state}\r\n");
        }

        private void HandleSetVariable(BatchCommand command) {
            string name = command.Value;
            string value = command.Arguments;
            _processManager.SetEnvironmentVariable(name, value);
            
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BatchExecutor: SET {Name}={Value}", name, value);
            }
        }

        private void HandleShowVariables() {
            Dictionary<string, string> allVars = _processManager.GetAllEnvironmentVariables();
            foreach (KeyValuePair<string, string> kvp in allVars) {
                PrintToConsole($"{kvp.Key}={kvp.Value}\r\n");
            }
        }

        private void HandleShowVariable(BatchCommand command) {
            string name = command.Value;
            string? value = _processManager.GetEnvironmentVariable(name);
            if (value is not null) {
                PrintToConsole($"{name}={value}\r\n");
            }
        }

        private void HandlePause() {
            PrintToConsole("Press any key to continue . . . ");
            
            // Wait for a key press using the file manager's standard input
            if (_fileManager.TryGetStandardInput(out CharacterDevice? stdIn) && stdIn.CanRead) {
                byte[] buffer = new byte[1];
                int bytesRead = stdIn.Read(buffer, 0, 1);
                if (bytesRead == 0 && _loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("BatchExecutor: No input available for PAUSE");
                }
            }
            
            PrintToConsole("\r\n");
        }

        private void PrintToConsole(string text) {
            if (_fileManager.TryGetStandardOutput(out CharacterDevice? stdOut) && stdOut.CanWrite) {
                byte[] bytes = Encoding.ASCII.GetBytes(text);
                stdOut.Write(bytes, 0, bytes.Length);
            } else if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BatchExecutor: Cannot write to console: {Text}", text);
            }
        }

        private readonly struct ProgramResolutionResult {
            public bool Found { get; }
            public string DosPath { get; }
            public string HostPath { get; }

            public static ProgramResolutionResult NotFound => new ProgramResolutionResult(false, string.Empty, string.Empty);

            public ProgramResolutionResult(bool found, string dosPath, string hostPath) {
                Found = found;
                DosPath = dosPath;
                HostPath = hostPath;
            }
        }
    }
}
