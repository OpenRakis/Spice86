namespace Spice86.Tests.Dos.Xms;

using FluentAssertions;

using Spice86.Shared.Interfaces;
using Spice86.Tests.Utility;

using Xunit;

/// <summary>
/// Integration tests for XMS functionality that run machine code through the emulation stack,
/// similar to how real programs like HITEST.ASM interact with the XMS driver.
/// </summary>
public class XmsIntegrationTests {
    enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests XMS installation check via INT 2Fh, AH=43h, AL=00h
    /// </summary>
    [Fact]
    public void XmsInstallationCheck_ShouldBeInstalled() {
        // Arrange
        string resourceName = "xms_installation_check.com";

        // Act
        TestIoPortHandler testHandler = RunXmsResource(resourceName, false);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests XMS entry point retrieval via INT 2Fh, AH=43h, AL=10h
    /// </summary>
    [Fact]
    public void GetXmsEntryPoint_ShouldReturnValidAddress() {
        // Arrange
        string resourceName = "xms_entry_point.com";

        // Act
        TestIoPortHandler testHandler = RunXmsResource(resourceName, false);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    [Fact]
    public void RequestAndReleaseUmb_RestoresDosAllocationState() {
        // Arrange
        string resourceName = "xms_request_release_umb.com";

        // Act
        TestIoPortHandler testHandler = RunXmsResource(resourceName, false);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    [Fact]
    public void RequestUmb_WhenDisabled_ReturnsNotImplemented() {
        // Arrange
        string resourceName = "xms_umb_disabled.com";

        // Act
        TestIoPortHandler testHandler = RunXmsResource(resourceName, false, false, false);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Runs the XMS test program and returns a test handler with results
    /// </summary>
    private TestIoPortHandler RunXmsResource(string resourceName, bool enableA20Gate) {
        return RunXmsResource(resourceName, enableA20Gate, false, true);
    }

    private TestIoPortHandler RunXmsResource(string resourceName, bool enableA20Gate, bool enableEms,
        bool enableUmb) {
        string filePath = Path.Join(AppContext.BaseDirectory, "Resources", "XmsTests", resourceName);
        if (!string.Equals(Path.GetExtension(filePath), ".com", StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("XMS resource tests require a DOS COM program.", nameof(resourceName));
        }

        using Spice86Creator creator = new Spice86Creator(
            binName: filePath,
            enablePit: true,
            maxCycles: 100000L,
            installInterruptVectors: true,
            enableA20Gate: enableA20Gate,
            enableEms: enableEms,
            enableXms: true
        );
        creator.EnableUmb = enableUmb;
        using Spice86DependencyInjection spice86DependencyInjection = creator.Create();

        TestIoPortHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILogger>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

}