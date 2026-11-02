namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;

/// <summary>
/// The per-reason counts of the printed status-flag reads of generated methods. Every read adds one
/// <see cref="FlagSetterKind"/>, one <see cref="FlagSetterDistance"/> and, unless the read has no single
/// setter, one <see cref="FlagPropagationBlocker"/>.
/// </summary>
internal sealed class FlagReadCensus {
    private readonly int[] _kinds = new int[Enum.GetValues<FlagSetterKind>().Length];
    private readonly int[] _distances = new int[Enum.GetValues<FlagSetterDistance>().Length];
    private readonly int[] _blockers = new int[Enum.GetValues<FlagPropagationBlocker>().Length];

    /// <summary>The number of counted flag reads.</summary>
    public int Total { get; private set; }

    /// <summary>Adds one flag read; <paramref name="blocker"/> is set for single setters only.</summary>
    public void Add(FlagSetterKind kind, FlagSetterDistance distance, FlagPropagationBlocker? blocker) {
        _kinds[(int)kind]++;
        _distances[(int)distance]++;
        if (blocker is FlagPropagationBlocker value) {
            _blockers[(int)value]++;
        }
        Total++;
    }

    /// <summary>Adds every count of <paramref name="other"/> to this census.</summary>
    public void Merge(FlagReadCensus other) {
        for (int index = 0; index < _kinds.Length; index++) {
            _kinds[index] += other._kinds[index];
        }
        for (int index = 0; index < _distances.Length; index++) {
            _distances[index] += other._distances[index];
        }
        for (int index = 0; index < _blockers.Length; index++) {
            _blockers[index] += other._blockers[index];
        }
        Total += other.Total;
    }

    /// <summary>The number of reads counted with <paramref name="kind"/>.</summary>
    public int Count(FlagSetterKind kind) => _kinds[(int)kind];

    /// <summary>The number of reads counted with <paramref name="distance"/>.</summary>
    public int Count(FlagSetterDistance distance) => _distances[(int)distance];

    /// <summary>The number of reads counted with <paramref name="blocker"/>.</summary>
    public int Count(FlagPropagationBlocker blocker) => _blockers[(int)blocker];
}
