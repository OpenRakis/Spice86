namespace Spice86.Tests.InterruptHandlers;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// The union of every service contract test case, shared by the contract test classes.
/// </summary>
public static class ServiceContractCoverageTest {
    internal static IEnumerable<ServiceContractCase> AllCases {
        get {
            foreach (IEnumerable<ServiceContractCase> cases in new[] {
                DosInt21ServiceContractTest.ContractCases,
                VgaBiosServiceContractTest.ContractCases,
                MouseInt33ServiceContractTest.ContractCases,
                EmsServiceContractTest.ContractCases,
                BiosInt13ServiceContractTest.ContractCases,
                BiosInt15ServiceContractTest.ContractCases,
                BiosInt14ServiceContractTest.ContractCases,
                BiosInt17ServiceContractTest.ContractCases,
                KeyboardInt16ServiceContractTest.ContractCases,
                SystemClockInt1AServiceContractTest.ContractCases,
                DosInt2AServiceContractTest.ContractCases,
                DosInt2FServiceContractTest.ContractCases,
                MiscVectorsServiceContractTest.ContractCases
            }) {
                foreach (ServiceContractCase testCase in cases) {
                    yield return testCase;
                }
            }
        }
    }
}
