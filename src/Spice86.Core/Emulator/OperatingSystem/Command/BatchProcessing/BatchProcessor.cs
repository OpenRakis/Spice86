namespace Spice86.Core.Emulator.OperatingSystem.Command.BatchProcessing;

using Serilog.Events;

using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Processes DOS batch files (.BAT) for COMMAND.COM.
/// </summary>
/// <remarks>
/// <para>
/// This class implements DOS batch file processing as part of the COMMAND.COM emulation.
/// Based on DOSBox staging's BatchFile class and FreeDOS FREECOM batch.c implementation.
/// </para>
/// <para>
/// Supported batch features:
/// <list type="bullet">
/// <item>ECHO ON/OFF command to control command echoing</item>
/// <item>@ prefix to suppress echoing of a single line</item>
/// <item>Parameter substitution (%0-%9)</item>
/// <item>Environment variable expansion (%NAME%)</item>
/// <item>Comment lines starting with REM or ::</item>
/// <item>Labels starting with :</item>
/// <item>GOTO, CALL, SET, IF, SHIFT, PAUSE, EXIT commands</item>
/// </list>
/// </para>
/// <para>
/// Reference implementations:
/// <list type="bullet">
/// <item>DOSBox Staging: https://github.com/dosbox-staging/dosbox-staging</item>
/// <item>FreeDOS FREECOM: https://github.com/FDOS/freecom</item>
/// <item>FreeDOS Kernel: https://github.com/FDOS/kernel</item>
/// </list>
/// </para>
/// </remarks>
public sealed class BatchProcessor : IDisposable {
    private readonly ILoggerService _loggerService;
    private readonly IBatchEnvironment _environment;

    /// <summary>
    /// Special separator characters for ECHO command.
    /// These characters can immediately follow ECHO to output the rest of the line.
    /// For example: ECHO. outputs an empty line, ECHO:hello outputs "hello".
    /// Based on FreeDOS FREECOM echo.c behavior.
    /// </summary>
    private static readonly char[] EchoSeparators = ['.', ',', ':', ';', '/', '[', '+', '(', '='];

    /// <summary>
    /// The ECHO state for the current batch context.
    /// When true, commands are echoed to stdout before execution.
    /// </summary>
    private bool _echoState = true;

    /// <summary>
    /// The current batch file being processed, or null if none.
    /// </summary>
    private BatchContext? _currentContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchProcessor"/> class.
    /// </summary>
    /// <param name="loggerService">The logger service for diagnostic output.</param>
    public BatchProcessor(ILoggerService loggerService)
        : this(loggerService, EmptyBatchEnvironment.Instance) {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchProcessor"/> class with environment support.
    /// </summary>
    /// <param name="loggerService">The logger service for diagnostic output.</param>
    /// <param name="environment">The environment provider for variable expansion.</param>
    public BatchProcessor(ILoggerService loggerService, IBatchEnvironment environment) {
        _loggerService = loggerService;
        _environment = environment;
    }

    /// <summary>
    /// Gets or sets the echo state.
    /// When true, commands are echoed to stdout before execution.
    /// </summary>
    public bool Echo {
        get => _echoState;
        set => _echoState = value;
    }

    /// <summary>
    /// Gets whether a batch file is currently being processed.
    /// </summary>
    public bool IsProcessingBatch => _currentContext is not null;

    /// <summary>
    /// Gets the current batch file path, or null if none.
    /// </summary>
    public string? CurrentBatchPath => _currentContext?.FilePath;

    /// <summary>
    /// Starts processing a batch file using the host file system.
    /// </summary>
    /// <param name="batchFilePath">The full path to the batch file.</param>
    /// <param name="arguments">Command line arguments passed to the batch file.</param>
    /// <returns>True if the batch file was successfully opened, false otherwise.</returns>
    public bool StartBatch(string batchFilePath, string[] arguments) {
        if (string.IsNullOrWhiteSpace(batchFilePath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BatchProcessor: Cannot start batch with empty path");
            }
            return false;
        }

        if (!File.Exists(batchFilePath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BatchProcessor: Batch file not found: {Path}", batchFilePath);
            }
            return false;
        }

        // Create a host file reader and start the batch
        IBatchLineReader reader = new HostFileLineReader(batchFilePath);
        return StartBatchWithReader(batchFilePath, arguments, reader);
    }

    /// <summary>
    /// Starts processing a batch file using a custom line reader.
    /// </summary>
    /// <param name="batchFilePath">The path identifier for the batch file (for %0 and logging).</param>
    /// <param name="arguments">Command line arguments passed to the batch file.</param>
    /// <param name="reader">The line reader for accessing the batch file content.</param>
    /// <returns>True if the batch file was successfully initialized, false otherwise.</returns>
    /// <remarks>
    /// This method allows starting a batch file with different sources:
    /// - Host file system (HostFileLineReader)
    /// - DOS file system (future implementation)
    /// - In-memory content (for testing)
    /// </remarks>
    public bool StartBatchWithReader(string batchFilePath, string[] arguments, IBatchLineReader reader) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "BatchProcessor: Starting batch file '{Path}' with {ArgCount} arguments",
                batchFilePath, arguments.Length);
        }

        // Create a new batch context with the provided reader
        BatchContext newContext = new(batchFilePath, arguments, _echoState, reader, _environment);

        // If there's an existing context, chain it (for nested batch files via CALL)
        if (_currentContext is not null) {
            newContext.Parent = _currentContext;
        }

        _currentContext = newContext;
        return true;
    }

    /// <summary>
    /// Reads the next line from the current batch file.
    /// </summary>
    /// <param name="shouldEcho">
    /// Set to true if this line should be echoed before execution,
    /// false if it should be executed silently (due to @ prefix or ECHO OFF).
    /// </param>
    /// <returns>
    /// The next command line to execute, or null if the batch file is complete or an error occurred.
    /// </returns>
    public string? ReadNextLine(out bool shouldEcho) {
        shouldEcho = false;

        if (_currentContext is null) {
            return null;
        }

        string? line = _currentContext.ReadLine();
        if (line is null) {
            // End of file or error - exit this batch context
            ExitBatch();
            return null;
        }

        // Trim whitespace
        line = line.Trim();

        // Skip empty lines
        if (string.IsNullOrEmpty(line)) {
            return ReadNextLine(out shouldEcho); // Recurse to get next line
        }

        // Check for @ prefix (suppress echo for this line)
        bool suppressEcho = false;
        if (line.StartsWith('@')) {
            suppressEcho = true;
            line = line[1..].TrimStart();
        }

        // Skip label lines (start with :)
        if (line.StartsWith(':')) {
            return ReadNextLine(out shouldEcho); // Recurse to get next line
        }

        // Skip REM comments
        if (IsRem(line)) {
            return ReadNextLine(out shouldEcho); // Recurse to get next line
        }

        // Determine if we should echo this line
        shouldEcho = _echoState && !suppressEcho;

        // Expand parameters (%0-%9)
        line = ExpandParameters(line, _currentContext);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BatchProcessor: Read line: '{Line}', shouldEcho={ShouldEcho}",
                line, shouldEcho);
        }

        return line;
    }

    /// <summary>
    /// Processes a batch command line and determines what action to take.
    /// </summary>
    /// <param name="commandLine">The command line to process.</param>
    /// <returns>A <see cref="BatchCommand"/> describing the action to take.</returns>
    public BatchCommand ParseCommand(string commandLine) {
        if (string.IsNullOrWhiteSpace(commandLine)) {
            return BatchCommand.Empty();
        }

        // Split the command line into command and arguments
        SplitCommand(commandLine, out string command, out string arguments);

        // Handle internal batch commands
        string upperCommand = command.ToUpperInvariant();

        // Special handling for ECHO with special separators (e.g., ECHO., ECHO:, etc.)
        // These must be handled before checking for exact "ECHO" match
        if (upperCommand.StartsWith("ECHO") && upperCommand.Length > 4) {
            char separator = command[4];
            if (IsEchoSeparator(separator)) {
                // ECHO<separator><message> - print the rest after the separator
                string message = command[5..];
                if (!string.IsNullOrEmpty(arguments)) {
                    message = message + " " + arguments;
                }
                return BatchCommand.PrintMessage(message.TrimStart());
            }
        }

        if (upperCommand == "ECHO") {
            return HandleEchoCommand(arguments);
        }

        if (upperCommand == "REM") {
            return BatchCommand.Empty(); // REM is a no-op
        }

        if (upperCommand == "GOTO") {
            return HandleGotoCommand(arguments);
        }

        if (upperCommand == "CALL") {
            return HandleCallCommand(arguments);
        }

        if (upperCommand == "SET") {
            return HandleSetCommand(arguments);
        }

        if (upperCommand == "IF") {
            return HandleIfCommand(arguments);
        }

        if (upperCommand == "SHIFT") {
            return HandleShiftCommand();
        }

        if (upperCommand == "PAUSE") {
            return BatchCommand.Pause();
        }

        if (upperCommand == "EXIT") {
            return BatchCommand.Exit();
        }

        if (upperCommand == "FOR") {
            return HandleForCommand(arguments);
        }

        // External command - execute program
        return BatchCommand.ExecuteProgram(command, arguments);
    }

    /// <summary>
    /// Exits the current batch file context.
    /// If there's a parent context (from CALL), returns to it.
    /// </summary>
    public void ExitBatch() {
        if (_currentContext is null) {
            return;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("BatchProcessor: Exiting batch file '{Path}'",
                _currentContext.FilePath);
        }

        // Restore echo state from the saved value
        _echoState = _currentContext.SavedEchoState;

        // If there's a parent context, return to it
        BatchContext? parent = _currentContext.Parent;
        _currentContext.Dispose();
        _currentContext = parent;
    }

    /// <summary>
    /// Disposes all batch contexts and releases resources.
    /// </summary>
    public void Dispose() {
        while (_currentContext is not null) {
            ExitBatch();
        }
    }

    /// <summary>
    /// Seeks to a label in the current batch file for GOTO command.
    /// </summary>
    /// <param name="label">The label to seek to (without the leading colon).</param>
    /// <returns>True if the label was found, false otherwise.</returns>
    public bool GotoLabel(string label) {
        if (_currentContext is null || string.IsNullOrWhiteSpace(label)) {
            return false;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BatchProcessor: GOTO searching for label '{Label}'", label);
        }

        return _currentContext.SeekToLabel(label);
    }

    /// <summary>
    /// Checks if the given line is a REM comment.
    /// </summary>
    private static bool IsRem(string line) {
        if (line.Length < 3) {
            return false;
        }

        string upper = line.ToUpperInvariant();
        
        // Check for "REM" followed by whitespace or end of line
        if (upper.StartsWith("REM") && (line.Length == 3 || char.IsWhiteSpace(line[3]))) {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Expands parameter placeholders (%0-%9) and environment variables (%NAME%) in the command line.
    /// </summary>
    /// <remarks>
    /// Based on DOSBox staging's ExpandedBatchLine() method.
    /// Supports:
    /// - %0-%9 for batch file parameters
    /// - %% for literal percent sign
    /// - %NAME% for environment variables
    /// </remarks>
    private static string ExpandParameters(string line, BatchContext context) {
        StringBuilder result = new(line.Length);
        int i = 0;

        while (i < line.Length) {
            if (line[i] == '%' && i + 1 < line.Length) {
                char next = line[i + 1];
                
                // Check for %0-%9
                if (next >= '0' && next <= '9') {
                    int paramIndex = next - '0';
                    string value = context.GetParameter(paramIndex);
                    result.Append(value);
                    i += 2;
                    continue;
                }
                
                // Check for %% (escaped percent)
                if (next == '%') {
                    result.Append('%');
                    i += 2;
                    continue;
                }

                // Check for %NAME% environment variable
                int closingPercent = line.IndexOf('%', i + 1);
                if (closingPercent > i + 1) {
                    string varName = line[(i + 1)..closingPercent];
                    string? envValue = context.GetEnvironmentValue(varName);
                    if (envValue is not null) {
                        result.Append(envValue);
                    }
                    // Undefined variables expand to empty string (DOS behavior)
                    i = closingPercent + 1;
                    continue;
                } else {
                    // No closing %, append literal %
                    result.Append('%');
                    i++;
                    continue;
                }
            }

            result.Append(line[i]);
            i++;
        }

        return result.ToString();
    }

    /// <summary>
    /// Splits a command line into command and arguments.
    /// </summary>
    private static void SplitCommand(string commandLine, out string command, out string arguments) {
        commandLine = commandLine.Trim();
        
        int spaceIndex = -1;
        for (int i = 0; i < commandLine.Length; i++) {
            if (char.IsWhiteSpace(commandLine[i])) {
                spaceIndex = i;
                break;
            }
        }

        if (spaceIndex == -1) {
            command = commandLine;
            arguments = string.Empty;
        } else {
            command = commandLine[..spaceIndex];
            arguments = commandLine[(spaceIndex + 1)..].TrimStart();
        }
    }

    /// <summary>
    /// Handles the ECHO command.
    /// </summary>
    private BatchCommand HandleEchoCommand(string arguments) {
        // Trim arguments for comparison
        string upperArgs = arguments.Trim().ToUpperInvariant();

        // ECHO without arguments shows current state
        if (string.IsNullOrEmpty(upperArgs)) {
            return BatchCommand.ShowEchoState(_echoState);
        }

        // ECHO ON
        if (upperArgs == "ON") {
            _echoState = true;
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BatchProcessor: ECHO ON");
            }
            return BatchCommand.Empty();
        }

        // ECHO OFF
        if (upperArgs == "OFF") {
            _echoState = false;
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BatchProcessor: ECHO OFF");
            }
            return BatchCommand.Empty();
        }

        // Check for ECHO separators (display empty line or message)
        // This handles "ECHO " followed by a special separator character
        if (arguments.Length > 0 && IsEchoSeparator(arguments[0])) {
            // These are all valid separators that print the rest as-is
            return BatchCommand.PrintMessage(arguments[1..]);
        }

        // ECHO <message> - print the message
        return BatchCommand.PrintMessage(arguments);
    }

    /// <summary>
    /// Checks if a character is a valid ECHO separator.
    /// </summary>
    private static bool IsEchoSeparator(char c) {
        return Array.IndexOf(EchoSeparators, c) >= 0;
    }

    /// <summary>
    /// Handles the GOTO command.
    /// </summary>
    private BatchCommand HandleGotoCommand(string arguments) {
        string label = arguments.Trim();
        if (string.IsNullOrEmpty(label)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BatchProcessor: GOTO without label");
            }
            return BatchCommand.Empty();
        }

        // Remove leading colon if present
        if (label.StartsWith(':')) {
            label = label[1..];
        }

        return BatchCommand.Goto(label);
    }

    /// <summary>
    /// Handles the CALL command.
    /// </summary>
    private static BatchCommand HandleCallCommand(string arguments) {
        SplitCommand(arguments, out string batchFile, out string batchArgs);
        return BatchCommand.CallBatch(batchFile, batchArgs);
    }

    /// <summary>
    /// Handles the SET command.
    /// </summary>
    /// <remarks>
    /// SET without arguments shows all environment variables.
    /// SET NAME shows the value of NAME.
    /// SET NAME=VALUE sets NAME to VALUE.
    /// </remarks>
    private static BatchCommand HandleSetCommand(string arguments) {
        string trimmed = arguments.Trim();

        // SET without arguments - show all variables
        if (string.IsNullOrEmpty(trimmed)) {
            return BatchCommand.ShowVariables();
        }

        // Find the equals sign
        int equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex == -1) {
            // SET NAME - show specific variable
            return BatchCommand.ShowVariable(trimmed.ToUpperInvariant());
        }

        // SET NAME=VALUE
        string name = trimmed[..equalsIndex].Trim().ToUpperInvariant();
        string value = trimmed[(equalsIndex + 1)..];

        if (string.IsNullOrEmpty(name)) {
            return BatchCommand.Empty();
        }

        return BatchCommand.SetVariable(name, value);
    }

    /// <summary>
    /// Handles the IF command.
    /// </summary>
    /// <remarks>
    /// Supports:
    /// - IF [NOT] EXIST filename command
    /// - IF [NOT] ERRORLEVEL number command
    /// - IF [NOT] string1==string2 command
    /// </remarks>
    private BatchCommand HandleIfCommand(string arguments) {
        string trimmed = arguments.Trim();
        bool negate = false;

        // Check for NOT
        if (trimmed.StartsWith("NOT ", StringComparison.OrdinalIgnoreCase)) {
            negate = true;
            trimmed = trimmed[4..].TrimStart();
        }

        // Check for EXIST
        if (trimmed.StartsWith("EXIST ", StringComparison.OrdinalIgnoreCase)) {
            string rest = trimmed[6..].TrimStart();
            return BatchCommand.If("EXIST", rest, negate);
        }

        // Check for ERRORLEVEL
        if (trimmed.StartsWith("ERRORLEVEL ", StringComparison.OrdinalIgnoreCase)) {
            string rest = trimmed[11..].TrimStart();
            return BatchCommand.If("ERRORLEVEL", rest, negate);
        }

        // String comparison (string1==string2)
        int doubleEquals = trimmed.IndexOf("==", StringComparison.Ordinal);
        if (doubleEquals > 0) {
            return BatchCommand.If("COMPARE", trimmed, negate);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("BatchProcessor: Invalid IF syntax: {Arguments}", arguments);
        }
        return BatchCommand.Empty();
    }

    /// <summary>
    /// Handles the SHIFT command.
    /// </summary>
    private BatchCommand HandleShiftCommand() {
        if (_currentContext is not null) {
            _currentContext.Shift();
        }
        return BatchCommand.Shift();
    }

    /// <summary>
    /// Handles the FOR command.
    /// FOR %variable IN (set) DO command
    /// </summary>
    /// <remarks>
    /// The FOR command iterates over a set of values, substituting each value
    /// for the variable in the command and executing it.
    /// </remarks>
    private BatchCommand HandleForCommand(string arguments) {
        // Parse: %variable IN (set) DO command
        string trimmed = arguments.Trim();
        
        // Find the variable (must start with %)
        int firstSpace = trimmed.IndexOf(' ');
        if (firstSpace < 0 || !trimmed.StartsWith('%') || trimmed.Length < 2) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BatchProcessor: Invalid FOR syntax - missing variable: {Arguments}", arguments);
            }
            return BatchCommand.Empty();
        }

        string variable = trimmed[..firstSpace].Trim();
        string rest = trimmed[firstSpace..].Trim();

        // Check for IN keyword
        if (!rest.StartsWith("IN ", StringComparison.OrdinalIgnoreCase) &&
            !rest.StartsWith("IN(", StringComparison.OrdinalIgnoreCase)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BatchProcessor: Invalid FOR syntax - missing IN keyword: {Arguments}", arguments);
            }
            return BatchCommand.Empty();
        }

        // Skip "IN" and optional whitespace
        int inIndex = rest.IndexOf("IN", StringComparison.OrdinalIgnoreCase);
        rest = rest[(inIndex + 2)..].TrimStart();

        // Find the set in parentheses
        if (!rest.StartsWith('(')) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BatchProcessor: Invalid FOR syntax - missing opening parenthesis: {Arguments}", arguments);
            }
            return BatchCommand.Empty();
        }

        int closeParenIndex = rest.IndexOf(')');
        if (closeParenIndex < 0) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BatchProcessor: Invalid FOR syntax - missing closing parenthesis: {Arguments}", arguments);
            }
            return BatchCommand.Empty();
        }

        string setContent = rest[1..closeParenIndex].Trim();
        rest = rest[(closeParenIndex + 1)..].Trim();

        // Check for DO keyword
        if (!rest.StartsWith("DO ", StringComparison.OrdinalIgnoreCase) &&
            !rest.StartsWith("DO\t", StringComparison.OrdinalIgnoreCase)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BatchProcessor: Invalid FOR syntax - missing DO keyword: {Arguments}", arguments);
            }
            return BatchCommand.Empty();
        }

        // Get the command after DO
        string commandTemplate = rest[3..].TrimStart();

        if (string.IsNullOrWhiteSpace(commandTemplate)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BatchProcessor: Invalid FOR syntax - missing command after DO: {Arguments}", arguments);
            }
            return BatchCommand.Empty();
        }

        // Parse the set (split by spaces, commas, semicolons, equals, tabs)
        List<string> setItems = ParseForSet(setContent);

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BatchProcessor: FOR {Variable} IN ({Set}) DO {Command}",
                variable, string.Join(", ", setItems), commandTemplate);
        }

        return BatchCommand.For(variable, setItems.ToArray(), commandTemplate);
    }

    /// <summary>
    /// Parses the set content for a FOR command.
    /// </summary>
    /// <remarks>
    /// Items can be separated by spaces, commas, semicolons, equals, or tabs.
    /// </remarks>
    private static List<string> ParseForSet(string setContent) {
        List<string> items = new();
        StringBuilder current = new();
        bool inQuote = false;

        foreach (char c in setContent) {
            if (c == '"') {
                inQuote = !inQuote;
            } else if (!inQuote && (c == ' ' || c == ',' || c == ';' || c == '=' || c == '\t')) {
                if (current.Length > 0) {
                    items.Add(current.ToString());
                    current.Clear();
                }
            } else {
                current.Append(c);
            }
        }

        if (current.Length > 0) {
            items.Add(current.ToString());
        }

        return items;
    }
}
