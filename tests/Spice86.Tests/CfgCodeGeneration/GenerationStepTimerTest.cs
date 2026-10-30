namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Xunit;

public class GenerationStepTimerTest {
    [Fact]
    public void MeasureRecordsTheStepsInOrder() {
        GenerationStepTimer timer = new();

        timer.Measure("one", () => { });
        timer.Measure("two", () => { });

        timer.Steps.Select(step => step.Step).Should().Equal("one", "two");
        timer.Steps.Should().OnlyContain(step => step.Milliseconds >= 0);
    }

    [Fact]
    public void MeasureOfFuncReturnsTheValue() {
        GenerationStepTimer timer = new();

        int result = timer.Measure("compute", () => 42);

        result.Should().Be(42);
        timer.Steps.Select(step => step.Step).Should().Equal("compute");
    }

    [Fact]
    public void RecordFillsTheStepsAndTheTotalIsTheirSum() {
        GenerationStepTimer timer = new();

        timer.Record("lift", 5);
        timer.Record("ssa build 1", 1200);
        timer.Record("printing", 7);

        timer.Steps.Should().Equal(("lift", 5L), ("ssa build 1", 1200L), ("printing", 7L));
        timer.TotalMilliseconds.Should().Be(1212);
    }
}
