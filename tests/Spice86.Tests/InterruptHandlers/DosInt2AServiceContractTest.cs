namespace Spice86.Tests.InterruptHandlers;

using Xunit;

/// <summary>
/// Differential contract tests for the DOS INT 2Ah network services.
/// </summary>
public class DosInt2AServiceContractTest {
    /// <summary>One case per declared INT 2Ah service.</summary>
    public static IEnumerable<ServiceContractCase> ContractCases {
        get {
            yield return new ServiceContractCase { Vector = 0x2A, SelectorValue = 0x00 };
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
