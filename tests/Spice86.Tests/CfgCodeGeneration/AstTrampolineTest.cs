namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Xunit;

public class AstTrampolineTest {
    [Fact]
    public void Run_ProducesResultsInRecursiveOrder() {
        List<string> log = [];
        AstResult<int> result = new();
        AstTrampoline.Run(Sum(4, result, log));
        result.Value.Should().Be(10);
        log.Should().Equal("enter4", "enter3", "enter2", "enter1", "leave1", "leave2", "leave3", "leave4");
    }

    [Fact]
    public void Run_SurvivesDeepChains() {
        AstResult<int> result = new();
        AstTrampoline.Run(Chain(100_000, result));
        result.Value.Should().Be(100_000);
    }

    private static IEnumerable<AstSubcall> Sum(int depth, AstResult<int> result, List<string> log) {
        log.Add($"enter{depth}");
        if (depth <= 1) {
            result.Value = 1;
        } else {
            AstResult<int> child = new();
            yield return AstTrampoline.Call(Sum(depth - 1, child, log));
            result.Value = depth + child.Value;
        }
        log.Add($"leave{depth}");
    }

    private static IEnumerable<AstSubcall> Chain(int depth, AstResult<int> result) {
        if (depth <= 0) {
            result.Value = 0;
            yield break;
        }
        AstResult<int> child = new();
        yield return AstTrampoline.Call(Chain(depth - 1, child));
        result.Value = child.Value + 1;
    }
}
