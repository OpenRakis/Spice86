namespace Spice86.Tests.InterruptHandlers;

using Xunit;

/// <summary>
/// Differential contract tests for the BIOS INT 16h keyboard services.
/// </summary>
public class KeyboardInt16ServiceContractTest {
    /// <summary>One case per declared INT 16h service.</summary>
    public static IEnumerable<ServiceContractCase> ContractCases {
        get {
            int[] selectors = [0x01, 0x02, 0x03, 0x05, 0x11, 0x1D];
            foreach (int selector in selectors) {
                yield return new ServiceContractCase { Vector = 0x16, SelectorValue = selector };
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
