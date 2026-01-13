# DOS Batch File Support Implementation

## Overview
This implementation adds full DOS batch file command execution support to Spice86 without requiring an interactive COMMAND.COM. The batch file processor integrates with the existing DOS infrastructure (INT21H, Console device, file management) to provide complete batch file execution capabilities.

## Implemented Commands

### 1. ECHO Command
**Fully functional with all variations:**
- `@ECHO OFF` - Suppress command echoing (@ suppresses echo for that line)
- `ECHO ON` - Enable command echoing
- `ECHO` - Display current echo state ("ECHO is ON" or "ECHO is OFF")
- `ECHO message` - Display message
- `ECHO.` - Display empty line
- `ECHO:`, `ECHO;`, etc. - DOS-compatible echo separators

### 2. SET Command
**Complete environment variable support:**
- `SET` - Display all environment variables
- `SET NAME` - Display specific variable value
- `SET NAME=VALUE` - Set environment variable
- Variables are case-insensitive (DOS behavior)
- Environment variables persist across nested batch files

### 3. PAUSE Command
**Full implementation:**
- Displays "Press any key to continue . . . "
- Waits for keypress from standard input
- Outputs newline after keypress
- Integrates with Console device input

### 4. Other Commands (Previously Implemented)
- **GOTO** - Jump to label
- **CALL** - Execute nested batch file
- **IF** - Conditional execution (EXIST, ERRORLEVEL, string comparison)
- **FOR** - Loop over set of values
- **SHIFT** - Shift command-line parameters
- **EXIT** - Exit batch file
- **REM** - Comment line

## Architecture

### Components Modified

#### 1. DosProgramLoader.cs - BatchExecutor Class
Added command execution handlers:
- `HandlePrintMessage(BatchCommand)` - Outputs ECHO messages
- `HandleShowEchoState(BatchCommand)` - Displays ECHO state
- `HandleSetVariable(BatchCommand)` - Stores environment variable
- `HandleShowVariables()` - Lists all variables
- `HandleShowVariable(BatchCommand)` - Shows specific variable
- `HandlePause()` - Pause with key wait
- `PrintToConsole(string)` - Helper for console output

#### 2. DosProcessManager.cs
Added environment variable management:
- `SetEnvironmentVariable(string name, string value)` - Set variable
- `GetAllEnvironmentVariables()` - Get all variables as dictionary
- `GetEnvironmentVariable(string name)` - Already existed

#### 3. Integration Points
- **Console Output**: Uses `DosFileManager.TryGetStandardOutput()` to get `CharacterDevice`
- **Console Input**: Uses `DosFileManager.TryGetStandardInput()` for PAUSE
- **Environment Storage**: Uses `EnvironmentVariables` class (case-insensitive dictionary)
- **Line Endings**: All output uses DOS-style CRLF (`\r\n`)

## Example Batch File

```bat
@echo off
echo ==========================
echo   INSTALLATION SCRIPT
echo ==========================
echo.
SET DEST=C:\PROGRAMS
echo Installing to %DEST%...
IF EXIST config.sys goto hasconfig
echo No config file found
goto end

:hasconfig
echo Config file detected
echo Installation complete!

:end
echo.
pause
exit
```

This batch file demonstrates:
1. Command echo suppression (@echo off)
2. Message output (ECHO)
3. Empty line (ECHO.)
4. Variable setting (SET DEST=...)
5. Variable expansion (%DEST%)
6. Conditional execution (IF EXIST)
7. Label jumps (GOTO)
8. Labels (:hasconfig, :end)
9. Pause for user input (PAUSE)
10. Exit (EXIT)

## Testing

### Test Coverage
- **BatchProcessorTests.cs**: 68 tests for batch file parsing
- **BatchExecutorTests.cs**: 42 tests for command execution
- **BatchCommandExecutionTests.cs**: 9 new integration tests
- **Total**: 119 tests all passing

### Key Test Scenarios
1. ECHO command variations (on/off/message/state/dot)
2. Environment variable operations (set/get/list)
3. Parameter expansion (%0-%9, %VAR%)
4. Nested batch files (CALL)
5. Label navigation (GOTO)
6. Conditional execution (IF)
7. Loops (FOR)
8. Complex multi-command scenarios

## Limitations & Design Decisions

### What Was NOT Implemented (By Design)
1. **No Interactive COMMAND.COM**: As specified in requirements, there is no interactive command prompt. Batch files run to completion or until EXIT.

2. **No Direct User Input Commands**: Commands like `CHOICE` are not implemented since batch files run non-interactively (except PAUSE which waits for a single keypress).

3. **Limited Environment Variable Scope**: Variables are global to the emulator session, not process-specific (matches DOS behavior).

### Technical Constraints
1. **Synchronous Execution**: Batch files execute synchronously during DOS program loading. This is appropriate for the use case (loading DOS games with launcher batch files).

2. **No Background Execution**: Batch commands execute in the foreground as part of program loading flow.

3. **Standard I/O Only**: Commands interact only with standard input/output devices (CON). No file redirection (>, >>, <) is implemented.

## Implementation Notes

### DOS Compatibility
- Command names are case-insensitive
- Environment variable names are case-insensitive
- Parameter expansion follows DOS rules (%0=batch file path, %1-%9=arguments, %%=literal %)
- ECHO separators match DOS behavior (., :, ;, /, [, +, (, =)
- Line endings are CRLF (DOS standard)

### Error Handling
- Missing labels in GOTO generate warning but don't crash
- Undefined environment variables expand to empty string
- Missing files in IF EXIST evaluate to false
- Console output failures are logged but don't stop batch execution

### Performance Considerations
- Batch files are processed line-by-line with no lookahead
- Labels are found via linear search (matches DOS behavior)
- Environment variables use case-insensitive dictionary (O(1) lookup)
- No caching or optimization needed for typical DOS batch file sizes

## Future Enhancements (If Needed)

Potential additions if requirements expand:
1. **File Redirection**: `>`, `>>`, `<` operators
2. **Pipe Support**: `|` operator for command chaining
3. **Extended IF Syntax**: `/I` (case-insensitive), `EQU/NEQ/LSS/LEQ/GTR/GEQ`
4. **Extended FOR Syntax**: `/D` (directories), `/R` (recursive)
5. **CHOICE Command**: Interactive menu selection
6. **ERRORLEVEL Setting**: Proper error code propagation from external programs

## References

### Implementation Based On
- DOSBox Staging batch file implementation
- FreeDOS COMMAND.COM batch processing
- MS-DOS batch file semantics
- Existing Spice86 DOS infrastructure (INT21H, Console device, File system)

### Code Locations
- Batch parsing: `src/Spice86.Core/Emulator/OperatingSystem/Command/BatchProcessing/`
- Command execution: `src/Spice86.Core/Emulator/OperatingSystem/DosProgramLoader.cs`
- Environment variables: `src/Spice86.Core/Emulator/OperatingSystem/DosProcessManager.cs`
- Tests: `tests/Spice86.Tests/Dos/Batch*.cs`
