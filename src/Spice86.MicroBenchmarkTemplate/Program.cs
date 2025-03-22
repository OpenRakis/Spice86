namespace Spice86.MicroBenchmarkTemplate;

using Xunit;

/// <summary>
/// Optionally, you can use BenchmarkDotNet to test some code for performance. <br/> <br/>
/// see <see href="https://github.com/dotnet/performance/blob/main/docs/microbenchmark-design-guidelines.md" />
/// for guidelines on writing good and efficient microbenchmarks
/// </summary>
public class BenchmarkTest {
    private StateLikeClass _instance;

    [GlobalSetup]
    public void Setup() {
        _instance = new StateLikeClass { SS = 1, SP = 2 };
    }

    [Benchmark]
    public SegmentedAddress NonCached() => _instance.StackSegmentedAddress;

    [Benchmark]
    public SegmentedAddress Cached() => _instance.CachedSegmentedAddress;
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
