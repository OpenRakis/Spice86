namespace Spice86.Tests.InterruptHandlers;

using Xunit;

/// <summary>
/// Differential contract tests for the INT 33h mouse services.
/// </summary>
public class MouseInt33ServiceContractTest {
    /// <summary>One case per declared INT 33h service.</summary>
    public static IEnumerable<ServiceContractCase> ContractCases {
        get {
            int[] selectors = [
                0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0x0B, 0x0C, 0x0F, 0x13, 0x14, 0x1A, 0x1B, 0x1C, 0x21,
                0x24
            ];
            foreach (int selector in selectors) {
                yield return new ServiceContractCase { Vector = 0x33, SelectorValue = selector };
            }
        }
    }

    /// <summary>Theory data.</summary>
    public static IEnumerable<object[]> Cases => ContractCases.Select(c => new object[] { c });

    /// <summary>
    /// Each declared contract holds across two runs with different undeclared inputs.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void ServicesHonourDeclaredContract(ServiceContractCase testCase) {
        // Arrange / Act / Assert
        ServiceContractHarness.AssertContract(testCase);
    }
}
