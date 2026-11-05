namespace Spice86.Tests.InterruptHandlers;

using Xunit;

/// <summary>
/// Differential contract tests for the whole-vector interrupt handlers
/// (one behavior for the entire vector, no selector register).
/// </summary>
public class MiscVectorsServiceContractTest {
    /// <summary>One case per declared non-Unknown whole-vector service.</summary>
    public static IEnumerable<ServiceContractCase> ContractCases {
        get {
            // INT 11h equipment list, INT 12h memory size, INT 24h critical
            // error, INT 28h DOS idle, INT 70h RTC IRQ. INT 1Ch has no
            // InterruptHandler-derived handler to run (its stub is a bare iret).
            byte[] vectors = [0x11, 0x12, 0x24, 0x28, 0x70];
            foreach (byte vector in vectors) {
                yield return new ServiceContractCase { Vector = vector, SelectorValue = 0 };
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
