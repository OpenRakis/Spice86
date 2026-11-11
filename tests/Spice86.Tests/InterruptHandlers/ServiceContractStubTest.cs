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
}
