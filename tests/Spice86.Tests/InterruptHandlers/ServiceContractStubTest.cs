namespace Spice86.Tests.InterruptHandlers;

using Xunit;

/// <summary>
/// Runs every contract case through the vector's installed in-RAM stub: an <c>int</c>
/// at a scratch address steps the CPU until the stub's <c>iret</c> returns, then asserts
/// the same contract as the direct-callback harness. Covers register, flag, IF and memory
/// effects of stub code outside the C# callback.
/// </summary>
public class ServiceContractStubTest {
    /// <summary>One stub-mode contract case per contract test case.</summary>
    public static IEnumerable<object[]> Cases =>
        ServiceContractCoverageTest.AllCases.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(Cases))]
    public void ServicesHonourDeclaredContractThroughStub(ServiceContractCase testCase) {
        // Arrange / Act / Assert
        ServiceContractHarness.AssertContractThroughStub(testCase);
    }

    /// <summary>The whole post-service machine of every contract case, frozen before the typed
    /// service refactor. The captured line is committed; it is regenerated deliberately only
    /// when the pre-refactor behavior itself is meant to change.</summary>
    [Fact]
    public void ServicesMatchRecordedBaseline() {
        // Arrange / Act
        string[] actual = ServiceContractCoverageTest.AllCases.Select((testCase, index) =>
            $"{index:D4}\t{testCase}\t{ServiceContractHarness.CaptureBaseline(testCase)}").ToArray();
        string path = Path.Join(MachineGoldens.SourceResourcesDirectory(), "service-contract-baseline.tsv");
        // File.WriteAllLines(path, actual);

        // Assert
        Assert.Equal(File.ReadAllLines(path), actual);
    }
}
