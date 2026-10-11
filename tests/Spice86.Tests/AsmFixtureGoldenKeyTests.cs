namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Tests.AsmFixtures;

using Xunit;

public sealed class AsmFixtureGoldenKeyTests {
    [Theory]
    [InlineData(0xF000, "video", "video.ivt.spec")]
    [InlineData(0xD000, "video.handlersD000", "video.ivt.spec.handlersD000")]
    [InlineData(0xE000, "video.handlersE000", "video.ivt.spec.handlersE000")]
    public void InstalledHandlerSegmentsSelectDistinctGoldens(ushort segment, string machineKey, string generatedKey) {
        // Arrange
        AsmRunSettings settings = new() {
            BinName = "video",
            InstallInterruptVectors = true,
            AsmHandlersSegment = segment
        };
        AsmFixture fixture = new(settings, [0], AsmFixture.NoResultAssertion);

        // Act / Assert
        MachineTest.MachineGoldenKey(fixture).Should().Be(machineKey);
        GeneratedCodeMachineTestRunner.GoldenKey(settings).Should().Be(generatedKey);
    }

    [Fact]
    public void UninstalledHandlerSegmentDoesNotChangeGoldenKeys() {
        // Arrange
        AsmRunSettings settings = new() { BinName = "video", AsmHandlersSegment = 0xD000 };
        AsmFixture fixture = new(settings, [0], AsmFixture.NoResultAssertion);

        // Act / Assert
        MachineTest.MachineGoldenKey(fixture).Should().Be("video");
        GeneratedCodeMachineTestRunner.GoldenKey(settings).Should().Be("video.spec");
    }
}
