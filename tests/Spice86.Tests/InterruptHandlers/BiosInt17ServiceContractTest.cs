namespace Spice86.Tests.InterruptHandlers;

using Xunit;

/// <summary>
/// Differential contract tests for the BIOS INT 17h printer services.
/// </summary>
public class BiosInt17ServiceContractTest {
    /// <summary>One case per declared INT 17h service.</summary>
    public static IEnumerable<ServiceContractCase> ContractCases {
        get {
            int[] selectors = [0x00, 0x01, 0x02];
            foreach (int selector in selectors) {
                yield return new ServiceContractCase { Vector = 0x17, SelectorValue = selector };
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
