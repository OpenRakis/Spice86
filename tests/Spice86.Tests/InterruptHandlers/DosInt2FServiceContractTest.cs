namespace Spice86.Tests.InterruptHandlers;

using Xunit;

/// <summary>
/// Differential contract tests for the DOS INT 2Fh multiplex services.
/// </summary>
public class DosInt2FServiceContractTest {
    /// <summary>One case per declared non-Unknown INT 2Fh service.</summary>
    public static IEnumerable<ServiceContractCase> ContractCases {
        get {
            int[] selectors = [0x10, 0x16, 0x1A, 0x46];
            foreach (int selector in selectors) {
                yield return new ServiceContractCase { Vector = 0x2F, SelectorValue = selector };
            }
            // AL=0x10 is Get XMS Control Function Address: it writes ES/BX.
            yield return new ServiceContractCase {
                Vector = 0x2F, SelectorValue = 0x43, NeedsXms = true,
                Setup = machine => machine.Spice86.Machine.CpuState.AL = 0x10
            };
            // AL=1 is Query Free HMA Space: it writes BX/ES/DI.
            yield return new ServiceContractCase {
                Vector = 0x2F, SelectorValue = 0x4A,
                Setup = machine => machine.Spice86.Machine.CpuState.AL = 0x01
            };
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
