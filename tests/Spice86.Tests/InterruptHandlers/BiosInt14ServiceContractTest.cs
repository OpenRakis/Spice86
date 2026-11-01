namespace Spice86.Tests.InterruptHandlers;

using Xunit;

/// <summary>
/// Differential contract tests for the BIOS INT 14h serial port services.
/// </summary>
public class BiosInt14ServiceContractTest {
    /// <summary>One case per declared INT 14h service.</summary>
    public static IEnumerable<ServiceContractCase> ContractCases {
        get {
            int[] selectors = [0x00, 0x01, 0x02, 0x03];
            foreach (int selector in selectors) {
                yield return new ServiceContractCase { Vector = 0x14, SelectorValue = selector };
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
