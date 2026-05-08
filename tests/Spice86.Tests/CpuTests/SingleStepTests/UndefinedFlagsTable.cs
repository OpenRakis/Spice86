namespace Spice86.Tests.CpuTests.SingleStepTests;

using System.Globalization;

/// <summary>
/// Loads opcode metadata from the upstream SingleStepTests <c>80386.csv</c> file
/// to determine which EFLAGS bits are defined (and therefore comparable) for
/// each opcode. Bits documented as undefined for a given opcode must be ignored
/// when diffing the final CPU flags against the recorded expected value, because
/// real hardware leaves them in an unspecified state.
/// </summary>
/// <remarks>
/// CSV column <c>f_umask</c> is a 16-bit mask where bit value 1 means the flag
/// is defined for that opcode and should be compared, and bit value 0 means the
/// flag is undefined and must be ignored. Upper EFLAGS bits (above bit 15) are
/// always compared.
/// </remarks>
public class UndefinedFlagsTable {
    private const uint AllDefined = 0xFFFFFFFFu;
    private const uint UpperFlagsAlwaysCompared = 0xFFFF0000u;

    private readonly Dictionary<string, uint> _maskByKey = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the table from the given CSV file path.
    /// </summary>
    /// <param name="csvPath">Path to <c>80386.csv</c> (upstream SingleStepTests repo).</param>
    public UndefinedFlagsTable(string csvPath) {
        Load(csvPath);
    }

    /// <summary>
    /// Returns a 32-bit mask where bits set to 1 must be compared and bits set
    /// to 0 must be ignored when comparing EFLAGS for the given opcode key.
    /// </summary>
    /// <param name="opcodeKey">Opcode filename without the <c>.json</c> suffix
    /// as it appears inside the test zip (for example <c>01</c>, <c>0FA0</c>,
    /// <c>80.4</c>, <c>6780.3</c>).</param>
    public uint GetDefinedFlagsMask(string opcodeKey) {
        string key = NormalizeKey(opcodeKey);
        if (_maskByKey.TryGetValue(key, out uint mask)) {
            return mask;
        }
        return AllDefined;
    }

    private static string NormalizeKey(string opcodeKey) {
        string remaining = opcodeKey;
        while (remaining.Length >= 2) {
            string prefix = remaining.Substring(0, 2);
            if (string.Equals(prefix, "66", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prefix, "67", StringComparison.OrdinalIgnoreCase)) {
                remaining = remaining.Substring(2);
                continue;
            }
            break;
        }
        return remaining;
    }

    private void Load(string csvPath) {
        string[] lines = File.ReadAllLines(csvPath);
        if (lines.Length == 0) {
            return;
        }
        string[] header = lines[0].Split(',');
        int opIdx = Array.IndexOf(header, "op");
        int exIdx = Array.IndexOf(header, "ex");
        int umaskIdx = Array.IndexOf(header, "f_umask");
        if (opIdx < 0 || exIdx < 0 || umaskIdx < 0) {
            return;
        }
        for (int i = 1; i < lines.Length; i++) {
            string[] cells = lines[i].Split(',');
            if (cells.Length <= umaskIdx) {
                continue;
            }
            string op = cells[opIdx].Trim();
            if (op.Length == 0) {
                continue;
            }
            string ex = cells[exIdx].Trim();
            string umaskStr = cells[umaskIdx].Trim();
            string key = ex.Length == 0 ? op : $"{op}.{ex}";
            _maskByKey[key] = ParseUmask(umaskStr);
        }
    }

    private static uint ParseUmask(string umaskStr) {
        if (umaskStr.Length == 0) {
            return AllDefined;
        }
        string cleaned = umaskStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? umaskStr.Substring(2)
            : umaskStr;
        if (!ushort.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort value)) {
            return AllDefined;
        }
        return UpperFlagsAlwaysCompared | value;
    }
}
