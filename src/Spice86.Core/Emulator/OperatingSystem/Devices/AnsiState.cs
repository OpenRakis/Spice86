namespace Spice86.Core.Emulator.OperatingSystem.Devices;

/// <summary>
/// Tracks the mutable state of the ANSI.SYS escape sequence parser.
/// Modeled after the NANSI driver's internal variables (cur_attrib, saved_coords, param_buffer, etc.).
/// </summary>
public sealed class AnsiState {
    /// <summary>
    /// Maximum number of parameter bytes in a single CSI sequence.
    /// NANSI uses a 48-byte param_buffer; we match that size to support
    /// keyboard reassignment strings (ESC[p).
    /// </summary>
    public const int MaxParameters = 48;

    /// <summary>
    /// Current text attribute byte (foreground + background + bold/blink).
    /// Default is 0x07 (light gray on black), matching ANSI.SYS reset.
    /// </summary>
    public byte Attribute { get; set; } = 0x07;

    /// <summary>
    /// Saved cursor column (ESC[s). -1 means no position has been saved.
    /// </summary>
    public int SavedColumn { get; set; } = -1;

    /// <summary>
    /// Saved cursor row (ESC[s). -1 means no position has been saved.
    /// </summary>
    public int SavedRow { get; set; } = -1;

    /// <summary>
    /// The current phase of the escape sequence parser state machine.
    /// </summary>
    public AnsiParserPhase Phase { get; set; } = AnsiParserPhase.Normal;

    /// <summary>
    /// Collected parameter bytes for the current CSI sequence.
    /// </summary>
    public byte[] Parameters { get; } = new byte[MaxParameters];

    /// <summary>
    /// Index of the current parameter slot being built.
    /// </summary>
    public int ParameterCount { get; set; }

    /// <summary>
    /// Whether the CSI prefix characters '=' and '?' are still allowed.
    /// Set to true when entering CsiCollecting, cleared after the first
    /// non-prefix character. NANSI: f_get_args is a one-shot state.
    /// </summary>
    public bool PrefixAllowed { get; set; }

    /// <summary>
    /// Whether the next semicolon should be silently eaten without advancing
    /// the parameter slot. Set after a quoted string terminates, matching
    /// NANSI's f_eat_semi behavior.
    /// </summary>
    public bool EatNextSemicolon { get; set; }

    /// <summary>
    /// Line wrap at end-of-line flag. When true (default), characters written
    /// past the rightmost column wrap to the next line. When false, they
    /// overwrite the last column. NANSI: wrap_flag (default 1).
    /// </summary>
    public bool WrapFlag { get; set; } = true;

    /// <summary>
    /// The character that terminates the current quoted string.
    /// Only meaningful when Phase is StringCollecting.
    /// </summary>
    public byte StringTerminator { get; set; }

    /// <summary>
    /// Keyboard reassignment table mapping key codes to replacement byte sequences.
    /// For normal keys the key is the ASCII code; for function keys (AL=0)
    /// the key is the scan code shifted left by 8. NANSI: lookup table.
    /// </summary>
    public Dictionary<ushort, byte[]> KeyRedefinitions { get; } = new();

    /// <summary>
    /// Resets the parser back to Normal phase, clearing collected parameters
    /// and transient parser flags. Persistent state (Attribute, WrapFlag,
    /// SavedColumn/Row, KeyRedefinitions) is preserved.
    /// </summary>
    public void Reset() {
        Phase = AnsiParserPhase.Normal;
        Array.Clear(Parameters, 0, MaxParameters);
        ParameterCount = 0;
        PrefixAllowed = false;
        EatNextSemicolon = false;
        StringTerminator = 0;
    }
}

/// <summary>
/// The phases of the ANSI.SYS state machine, matching NANSI's
/// f_escape / f_bracket / f_get_param / f_get_string progression.
/// </summary>
public enum AnsiParserPhase : byte {
    /// <summary>
    /// Not inside an escape sequence. Characters are output directly.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// ESC has been received; waiting for '[' to form a CSI introducer.
    /// Corresponds to NANSI's f_escape / f_bracket states.
    /// </summary>
    EscapeReceived = 1,

    /// <summary>
    /// ESC[ (CSI) received; collecting parameters and waiting for the final command letter.
    /// Corresponds to NANSI's f_get_param / f_in_param states.
    /// </summary>
    CsiCollecting = 2,

    /// <summary>
    /// Inside a quoted string parameter. Characters are stored as individual
    /// parameter bytes until the matching terminator is found.
    /// Corresponds to NANSI's f_get_string state.
    /// </summary>
    StringCollecting = 3,
}
