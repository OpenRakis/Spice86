namespace Spice86.Tests.Bios;

using FluentAssertions;

using Spice86.Shared.Interfaces;
using Spice86.Tests.Utility;

using Xunit;

/// <summary>
/// Integration tests for BIOS INT 15h, AH=87h (Copy Extended Memory), run as real assembly code
/// through the emulation stack.
/// </summary>
public class SystemBiosInt15HandlerTests
{
    enum TestResult : byte
    {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// <see cref="Spice86.Core.Emulator.InterruptHandlers.Bios.SystemBiosInt15Handler.CopyExtendedMemory"/>
    /// always read/wrote through the shared <c>Memory</c> bus directly (no private XMS/EMS-style
    /// array), so it transparently gained the ability to address the full unified extended-memory
    /// pool once Ram was grown past the old ~1.06MB conventional+HMA ceiling. This round-trips a
    /// marker word through a linear address 3MB in, well beyond that old ceiling.
    /// </summary>
    [Fact]
    public void BiosInt15h_87h_ShouldCopyAcrossFullUnifiedMemoryRange()
    {
        string resourcePath = Path.Join(AppContext.BaseDirectory, "Resources", "BiosInt15Tests", "bios_int15h_87h.com");
        string cDrive = Path.GetDirectoryName(resourcePath) ?? AppContext.BaseDirectory;

        using Spice86Creator creator = new Spice86Creator(
            binName: resourcePath,
            installInterruptVectors: true,
            cDrive: cDrive
        );
        using Spice86DependencyInjection spice86DependencyInjection = creator.Create();

        TestIoPortHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILogger>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "copying a word to and from a linear address 3MB in should succeed");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
        testHandler.Details.Should().Contain(0x02, "both copy directions should have completed");
    }
}
