namespace Spice86.Core.Emulator.VM.Clock;

/// <summary>
/// Base class for clock jitter sources.
/// The dotnet JIT can devirtualize calls to sealed subclasses, making the no-op path and branch-free at runtime.
/// Use <see cref="Create"/> to obtain the appropriate instance.
/// </summary>
internal abstract class ClockJitter {
    /// <summary>
    /// Returns a <see cref="NoOpClockJitter"/> when <paramref name="seed"/> is <c>null</c>,
    /// or a deterministic <see cref="RandomClockJitter"/> otherwise.
    /// </summary>
    internal static ClockJitter Create(int? seed) =>
        seed.HasValue ? new RandomClockJitter(seed.Value) : new NoOpClockJitter();

    /// <summary>
    /// Returns a signed jitter offset in milliseconds to add to the clock's elapsed time.
    /// </summary>
    internal abstract double Advance();

    /// <summary>Jitter implementation that always returns zero; used when no seed is configured.</summary>
    private sealed class NoOpClockJitter : ClockJitter {
        internal override double Advance() => 0.0;
    }

    /// <summary>
    /// Jitter implementation that uses a fast xorshift64 PRNG to produce a bounded,
    /// deterministic offset in [<c>0</c>, <c>MaxJitterMs</c>].
    /// </summary>
    private sealed class RandomClockJitter : ClockJitter {
        private const double MaxJitterMs = 0.01;

        private const ulong GoldenRatio64 = 0x9E3779B97F4A7C15UL;

        private const double High53BitsToDoubleFactor = 1.0 / (1UL << 53);

        private ulong _state;

        internal RandomClockJitter(int seed) {
            // Initialize PRNG state from the 32-bit seed and spread entropy into 64 bits.
            // Combining with the golden-ratio constant reduces correlation for small seeds.
            ulong s = (uint)seed;
            _state = s ^ (s << 32) ^ GoldenRatio64;
            // xorshift algorithms require a non-zero state; avoid the degenerate zero value.
            if (_state == 0) {
                _state = GoldenRatio64;
            }
        }

        internal override double Advance() {
            // xorshift64 PRNG step: quick bit-mixing to produce the next pseudo-random state.
            _state ^= _state << 13;
            _state ^= _state >> 7;
            _state ^= _state << 17;
            // Convert the top 53 bits of the 64-bit PRNG state into a `double` in [0,1).
            // We shift right by 11 (64 - 53) to select those high 53 bits.
            // 53 matches the IEEE-754 `double` significand (52 fraction bits + implicit leading 1).
            // Then we divide byt 2^53 to normalize to [0,1).
            // Using the high-order bits yields better statistical quality for xorshift.
            double normalized = (_state >> 11) * High53BitsToDoubleFactor;
            return normalized * MaxJitterMs;
        }
    }
}
