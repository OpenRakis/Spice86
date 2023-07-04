namespace Spice86.Tests.Dos;

using Spice86.Core.Emulator.OperatingSystem;

using Xunit;

using Moq;
using FluentAssertions;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.CLI;
using Spice86.Shared.Interfaces;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Structures;

public class DosFileManagerTests {

    [Fact]
    public void GetCurrentDrive_IsValidFormat() {
        //Arrange
        string tempDir = Path.GetTempPath();
        if(!Directory.Exists($"{tempDir}/TEST")) {
            Directory.CreateDirectory($"{ tempDir}/TEST");
        }
        Configuration configuration = new Configuration() {
            DumpDataOnExit = false,
            CDrive = tempDir
        };
        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        IMemory memory = new Memory(ram, configuration);
        Mock<ILoggerService> loggerServiceMock = new Mock<ILoggerService>();
        Mock<IVirtualDevice> chracterDeviceMock = new Mock<IVirtualDevice>();
        List<IVirtualDevice> dosDevicesMock = new List<IVirtualDevice>() { chracterDeviceMock.Object };
        DosFileManager dosFileManager = new DosFileManager(new Memory(ram, configuration), loggerServiceMock.Object, dosDevicesMock, new DosPathResolver(loggerServiceMock.Object, configuration));

        //Act
        dosFileManager.SetCurrentDir("TEST");
        DosFileOperationResult result = dosFileManager.GetCurrentDir(0x0, out string currentDir);

        //Assert
        result.Should().BeEquivalentTo(DosFileOperationResult.NoValue());
        currentDir.Should().BeEquivalentTo("TEST").And.BeUpperCased();
    }
}
