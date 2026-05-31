namespace Spice86.MicroBenchmarkTemplate;

using System.Numerics;

using Xunit;

/// <summary>
/// Optionally, you can use BenchmarkDotNet to test some code for performance. <br/> <br/>
/// see <see href="https://github.com/dotnet/performance/blob/main/docs/microbenchmark-design-guidelines.md" />
/// for guidelines on writing good and efficient microbenchmarks
/// </summary>
public class BenchmarkTest {
    private StateLikeClass _instance = null!;

    [GlobalSetup]
    public void Setup() {
        _instance = new StateLikeClass { SS = 1, SP = 2 };
    }

    [Benchmark]
    public SegmentedAddress NonCached() => _instance.StackSegmentedAddress;

    [Benchmark]
    public SegmentedAddress Cached() => _instance.CachedSegmentedAddress;

    [Benchmark]
    [Arguments(42)]
    [Arguments(69)]
    public bool AluParity8_FourBit(byte value) {
        const uint FourBitParityTable = 0b1001011001101001;
        int low4 = value & 0xF;
        int high4 = value >> 4 & 0xF;
        return ((FourBitParityTable >> low4) & 1) == ((FourBitParityTable >> high4) & 1);
    }

    [Benchmark]
    [Arguments(42)]
    [Arguments(69)]
    public bool AluParity8_PopCount(byte value) {
        return (BitOperations.PopCount(value) & 1) == 0;
    }

    [Benchmark]
    [Arguments(42)]
    [Arguments(69)]
    public bool AluParity8_PopCountFallback(byte value) {
        // This simulates platforms which do not have a hardware-optimized population count implementation. The
        // "SoftwareFallback" method was copied from BitOperations.PopCount on .NET 10.
        return (SoftwareFallback(value) & 1) == 0;

        static int SoftwareFallback(uint value) {
            const uint c1 = 0x_55555555u;
            const uint c2 = 0x_33333333u;
            const uint c3 = 0x_0F0F0F0Fu;
            const uint c4 = 0x_01010101u;

            value -= (value >> 1) & c1;
            value = (value & c2) + ((value >> 2) & c2);
            value = (((value + (value >> 4)) & c3) * c4) >> 24;

            return (int)value;
        }
    }
}

internal class Program {
    public static void Main() {
#if RELEASE
        BenchmarkDotNet.Reports.Summary summary = BenchmarkRunner.Run<BenchmarkTest>();
#endif
#if DEBUG
        Assert.Fail("Please run in Release mode to get accurate results");
#endif
    }
}

public class StateLikeClass {
    private ushort _ss;
    private ushort _sp;
    private SegmentedAddress? _cachedSegmentedAddress;

    public ushort SS {
        get => _ss;
        set {
            _ss = value;
        }
    }

    public ushort SP {
        get => _sp;
        set {
            _sp = value;
        }
    }

    public SegmentedAddress StackSegmentedAddress => new(SS, SP);

    public SegmentedAddress CachedSegmentedAddress => _cachedSegmentedAddress ??= new SegmentedAddress(SS, SP);
}
