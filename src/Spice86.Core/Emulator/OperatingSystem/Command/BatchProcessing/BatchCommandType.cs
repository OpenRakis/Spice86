namespace Spice86.Core.Emulator.OperatingSystem.Command.BatchProcessing;

/// <summary>
/// Types of batch commands.
/// </summary>
public enum BatchCommandType {
    /// <summary>Empty command (no operation).</summary>
    Empty,

    /// <summary>Print a message to stdout.</summary>
    PrintMessage,

    /// <summary>Show the current ECHO state.</summary>
    ShowEchoState,

    /// <summary>Execute an external program.</summary>
    ExecuteProgram,

    /// <summary>GOTO a label.</summary>
    Goto,

    /// <summary>CALL another batch file.</summary>
    CallBatch,

    /// <summary>SET environment variable.</summary>
    SetVariable,

    /// <summary>Show all environment variables.</summary>
    ShowVariables,

    /// <summary>Show a specific environment variable.</summary>
    ShowVariable,

    /// <summary>IF conditional command.</summary>
    If,

    /// <summary>SHIFT parameters.</summary>
    Shift,

    /// <summary>PAUSE execution.</summary>
    Pause,

    /// <summary>EXIT batch file.</summary>
    Exit,

    /// <summary>FOR loop command.</summary>
    For
}
