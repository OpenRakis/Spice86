namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

/// <summary>The per-<see cref="GotoKeepReason"/> counts of the printed <c>goto</c>s of generated methods.</summary>
internal sealed class GotoCensus {
    private readonly int[] _counts = new int[Enum.GetValues<GotoKeepReason>().Length];

    /// <summary>The number of counted <c>goto</c>s.</summary>
    public int Total { get; private set; }

    /// <summary>Adds one <c>goto</c> of <paramref name="reason"/>.</summary>
    public void Add(GotoKeepReason reason) => Add(reason, 1);

    /// <summary>Adds <paramref name="count"/> <c>goto</c>s of <paramref name="reason"/>.</summary>
    public void Add(GotoKeepReason reason, int count) {
        _counts[(int)reason] += count;
        Total += count;
    }

    /// <summary>Adds every count of <paramref name="other"/> to this census.</summary>
    public void Merge(GotoCensus other) {
        for (int index = 0; index < _counts.Length; index++) {
            _counts[index] += other._counts[index];
        }
        Total += other.Total;
    }

    /// <summary>The number of <c>goto</c>s counted with <paramref name="reason"/>.</summary>
    public int Count(GotoKeepReason reason) => _counts[(int)reason];
}
