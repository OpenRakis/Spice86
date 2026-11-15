namespace Spice86.Tests.InterruptHandlers;

using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.IOPorts;

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
            // A second INT 70h run with a periodic interrupt pending and a BIOS WAIT
            // timeout to decrement: the handler reads and writes the BIOS data area,
            // which the contract's Memory = ReadWrite covers.
            yield return new ServiceContractCase {
                Vector = 0x70, SelectorValue = 0,
                Setup = machine => {
                    IOPortDispatcher io = machine.Spice86.Machine.IoPortDispatcher;
                    io.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterB);
                    io.WriteByte(CmosPorts.Data, 0x42);
                    io.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterC);
                    io.WriteByte(CmosPorts.Data, 0x40);
                    machine.Spice86.Machine.Memory.UInt32[0x49C] = 2000;
                    machine.Spice86.Machine.Memory.UInt8[0x4A0] = 1;
                }
            };
            // INT 9 with a scan code waiting in the 8042: the handler reads the
            // keyboard ports, updates the BDA keyboard flags and enqueues the key
            // code in the BIOS keyboard buffer, which the contract's
            // Memory = ReadWrite, Io = true covers.
            yield return new ServiceContractCase {
                Vector = 0x09, SelectorValue = 0,
                Setup = machine =>
                    machine.Spice86.Machine.KeyboardController.KeyboardDevice
                        .EnqueueKeyEvent(PcKeyboardKey.A, true)
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

    /// <summary>The IRQ 3 default handler's declared contract (its BDA record of the
    /// unexpected IRQ, its PIC acknowledge and mask) holds through its installed stub.
    /// Run through the stub only: <c>DefaultIrqHandler</c> is not an
    /// <c>InterruptHandler</c> and the callback mode cannot invoke it.</summary>
    [Fact]
    public void DefaultIrqHandlerHonoursDeclaredContractThroughStub() {
        // Arrange / Act / Assert
        ServiceContractHarness.AssertContractThroughStub(
            new ServiceContractCase { Vector = 0x0B, SelectorValue = 0 });
    }

    /// <summary>The mouse-driver user-routine callbacks' declared contracts hold on the
    /// no-routine path: <c>BeforeUserHandlerExecution</c> (0x10C) only writes the far
    /// call's target in memory and <c>AfterUserHandlerExecution</c> (0x10D) does
    /// nothing. A registered routine keeps them <c>Unknown</c> (IS-16).</summary>
    [Theory]
    [InlineData(0x10C)]
    [InlineData(0x10D)]
    public void MouseUserRoutineCallbacksHonourDeclaredContracts(ushort callbackNumber) {
        // Arrange / Act / Assert
        ServiceContractHarness.AssertContract(
            new ServiceContractCase { Vector = 0, SelectorValue = 0, CallbackNumber = callbackNumber });
    }
}
