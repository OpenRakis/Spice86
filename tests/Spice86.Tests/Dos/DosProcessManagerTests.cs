namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Mscdex;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Batch;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.Tests.Utility;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Xunit;

public class DosProcessManagerTests {
    private const ushort AdditionalEnvironmentStringsCount = 1;
    private const string ResourceDirectory = "DosProcessManagerTests";

    [Fact]
    public void CreateRootCommandComPsp_MatchesFreeDosLayout() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        DosProgramSegmentPrefix rootPsp = GetRootPsp(context);
        rootPsp.CurrentSize.Should().Be(
            (ushort)(DosProcessManager.CommandComSegment + DosProgramSegmentPrefix.PspSizeInParagraphs));
        rootPsp.EnvironmentTableSegment.Should().Be((ushort)(DosProcessManager.CommandComSegment + 8));
    }

    [Fact]
    public void CreateRootCommandComPsp_UsesSharedFilesLimit() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        DosProgramSegmentPrefix rootPsp = GetRootPsp(context);
        rootPsp.MaximumOpenFiles.Should().Be(DosFileManager.MaxOpenFilesPerProcess);
    }

    [Fact]
    public void LoadComChildPsp_ShouldClearUninitializedFields() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        DosProgramSegmentPrefix rootPsp = GetRootPsp(context);
        rootPsp.NNFlags = 0xABCD;

        using TempFile tempFile = new("DosProcessManagerTests");
        string comFilePath = CreateFileFromResource(tempFile, "load_child.com", "nop_sled_ret_1.com");
        DosExecParameterBlock parameterBlock = CreateParameterBlock();

        DosExecResult result = context.ProcessManager.LoadOrLoadAndExecute(
            comFilePath,
            parameterBlock,
            string.Empty,
            DosExecLoadType.LoadOnly,
            environmentSegment: 0);

        result.Success.Should().BeTrue();
        ushort childSegment = result.InitialCS;
        DosProgramSegmentPrefix childPsp = new(context.Memory, MemoryUtils.ToPhysicalAddress(childSegment, 0));
        childPsp.NNFlags.Should().Be(0);
    }

    [Fact]
    public void ExecLoadOnly_DoesNotAlterParentStackPointer() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        DosProgramSegmentPrefix rootPsp = GetRootPsp(context);
        rootPsp.StackPointer = new SegmentedAddress(0x1234, 0x5678);

        context.State.SS = 0x3000;
        context.State.SP = 0x00FE;

        using TempFile tempFile = new("DosProcessManagerTests");
        string comFilePath = CreateFileFromResource(tempFile, "program.com", "nop_sled_ret_1.com");
        DosExecParameterBlock parameterBlock = CreateParameterBlock();

        DosExecResult result = context.ProcessManager.LoadOrLoadAndExecute(
            comFilePath,
            parameterBlock,
            string.Empty,
            DosExecLoadType.LoadOnly,
            environmentSegment: 0);

        result.Success.Should().BeTrue();
        rootPsp.StackPointer.Should().Be(new SegmentedAddress(0x1234, 0x5678));
    }

    [Fact]
    public void TerminateProcess_RestoresParentStackPointer() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        using TempFile tempFile = new("DosProcessManagerTests");
        string comFilePath = CreateFileFromResource(tempFile, "program.com", "nop_sled_ret_1.com");
        DosExecParameterBlock parameterBlock = CreateParameterBlock();
        DosExecResult parentExec = context.ProcessManager.LoadOrLoadAndExecute(
            comFilePath,
            parameterBlock,
            string.Empty,
            DosExecLoadType.LoadAndExecute,
            environmentSegment: 0);

        parentExec.Success.Should().BeTrue();
        ushort parentSegment = context.CurrentPspSegment;

        // Shrink parent's memory to PSP + small code, freeing space for child
        const ushort parentMinimumSize = DosProgramSegmentPrefix.PspSizeInParagraphs + 0x10;
        DosErrorCode shrinkResult = context.MemoryManager.TryModifyBlock(
            parentSegment,
            parentMinimumSize,
            out DosMemoryControlBlock _);
        shrinkResult.Should().Be(DosErrorCode.NoError, "parent must shrink memory before loading child");

        context.State.SS = 0xFFFD;
        context.State.SP = 0xFFFE;

        parameterBlock = CreateParameterBlock();

        DosExecResult childExec = context.ProcessManager.LoadOrLoadAndExecute(
            comFilePath,
            parameterBlock,
            string.Empty,
            DosExecLoadType.LoadAndExecute,
            environmentSegment: 0);

        childExec.Success.Should().BeTrue();

        context.State.SS = 0x3333;
        context.State.SP = 0x0100;

        context.ProcessManager.TerminateProcess(0, DosTerminationType.Normal);

        context.CurrentPspSegment.Should().Be(parentSegment);
        context.State.SS.Should().Be(0xFFFD);
        // SP is restored to the exact value it had before the EXEC call.
        // Spice86's CfgCpu has no GP register frame (unlike FreeDOS's PUSH$ALL),
        // so no IregsFrameSize subtraction is applied.
        context.State.SP.Should().Be(0xFFFE);
    }

    [Fact]
    public void TerminateProcess_TsrFreesEnvironmentBlock() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        using TempFile tempFile = new("DosProcessManagerTests");
        string comFilePath = CreateFileFromResource(tempFile, "program.com", "nop_sled_ret_200.com");
        DosExecParameterBlock parameterBlock = CreateParameterBlock();

        DosExecResult execResult = context.ProcessManager.LoadOrLoadAndExecute(
            comFilePath,
            parameterBlock,
            string.Empty,
            DosExecLoadType.LoadAndExecute,
            environmentSegment: 0);

        execResult.Success.Should().BeTrue();

        ushort tsrSegment = context.CurrentPspSegment;
        DosProgramSegmentPrefix tsrPsp = new(context.Memory, MemoryUtils.ToPhysicalAddress(tsrSegment, 0));

        ushort environmentSegment = tsrPsp.EnvironmentTableSegment;
        environmentSegment.Should().NotBe(0);

        DosMemoryControlBlock environmentBlock = new(context.Memory, MemoryUtils.ToPhysicalAddress((ushort)(environmentSegment - 1), 0));
        environmentBlock.IsFree.Should().BeFalse();

        DosMemoryControlBlock residentBlock = new(context.Memory, MemoryUtils.ToPhysicalAddress((ushort)(tsrSegment - 1), 0));
        ushort requestedResidentSize = (ushort)(residentBlock.Size - 4);
        requestedResidentSize.Should().BeGreaterThan(DosProgramSegmentPrefix.PspSizeInParagraphs);

        DosErrorCode resizeResult = context.MemoryManager.TryModifyBlock(tsrSegment, requestedResidentSize, out DosMemoryControlBlock resizedBlock);
        resizeResult.Should().Be(DosErrorCode.NoError);
        resizedBlock.Size.Should().Be(requestedResidentSize);

        context.ProcessManager.TerminateProcess(0, DosTerminationType.TSR);

        environmentBlock = new(context.Memory, MemoryUtils.ToPhysicalAddress((ushort)(environmentSegment - 1), 0));
        environmentBlock.IsFree.Should().BeTrue("TSR termination should free the child environment block");
    }

    [Fact]
    public void TerminateProcess_TsrRetainsResidentBlockMetadata() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        using TempFile tempFile = new("DosProcessManagerTests");
        string comFilePath = CreateFileFromResource(tempFile, "program.com", "nop_sled_ret_200.com");
        DosExecParameterBlock parameterBlock = CreateParameterBlock();

        DosExecResult execResult = context.ProcessManager.LoadOrLoadAndExecute(
            comFilePath,
            parameterBlock,
            string.Empty,
            DosExecLoadType.LoadAndExecute,
            environmentSegment: 0);

        execResult.Success.Should().BeTrue();

        ushort tsrSegment = context.CurrentPspSegment;

        ushort paragraphsToKeep = DosProgramSegmentPrefix.PspSizeInParagraphs + 0x10;
        DosErrorCode resizeResult = context.MemoryManager.TryModifyBlock(
            tsrSegment,
            paragraphsToKeep,
            out DosMemoryControlBlock resizedBlock);

        resizeResult.Should().Be(DosErrorCode.NoError);
        context.ProcessManager.TrackResidentBlock(resizedBlock);

        context.ProcessManager.TerminateProcess(0x00, DosTerminationType.TSR);

        DosMemoryControlBlock residentBlock = new(context.Memory, MemoryUtils.ToPhysicalAddress((ushort)(tsrSegment - 1), 0));
        residentBlock.Size.Should().Be(paragraphsToKeep);
        residentBlock.PspSegment.Should().Be(tsrSegment);
        residentBlock.Owner.Should().Be(resizedBlock.Owner);
    }

    [Fact]
    public void LoadAndExecute_Com_ClonesEnvironmentWithinDosLimits() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        DosProgramSegmentPrefix rootPsp = GetRootPsp(context);

        byte[] largeEnvironment = BuildLargeEnvironmentBytes(DosProcessManager.MaximumEnvironmentScanLength - 2);
        ushort paragraphsNeeded = (ushort)Math.Max(1, (largeEnvironment.Length + 15) / 16);
        DosMemoryControlBlock? parentEnvBlock = context.MemoryManager.AllocateMemoryBlock(paragraphsNeeded);
        parentEnvBlock.Should().NotBeNull();

        DosMemoryControlBlock envBlock = parentEnvBlock ?? throw new InvalidOperationException("Unable to allocate parent environment block.");

        context.Memory.LoadData(MemoryUtils.ToPhysicalAddress(envBlock.DataBlockSegment, 0), largeEnvironment);
        rootPsp.EnvironmentTableSegment = envBlock.DataBlockSegment;

        using TempFile tempFile = new("DosProcessManagerTests");
        string comFilePath = CreateFileFromResource(tempFile, "program.com", "nop_sled_ret_1.com");
        DosExecParameterBlock parameterBlock = CreateParameterBlock();
        string expectedDosFileName = Path.GetFileName(comFilePath).ToUpperInvariant();

        DosExecResult execResult = context.ProcessManager.LoadOrLoadAndExecute(
            comFilePath,
            parameterBlock,
            string.Empty,
            DosExecLoadType.LoadAndExecute,
            environmentSegment: 0);

        execResult.Success.Should().BeTrue();

        ushort childSegment = context.CurrentPspSegment;
        DosProgramSegmentPrefix childPsp = new(context.Memory, MemoryUtils.ToPhysicalAddress(childSegment, 0));

        EnvironmentBlockInfo environmentInfo = ReadEnvironmentBlockInfo(context.Memory, childPsp.EnvironmentTableSegment);
        environmentInfo.TotalLength.Should().BeLessThanOrEqualTo(DosProcessManager.MaximumEnvironmentScanLength);
        environmentInfo.AdditionalStringCount.Should().Be(AdditionalEnvironmentStringsCount);
        environmentInfo.ProgramPath.Should().EndWith(expectedDosFileName);
    }

    [Fact]
    public void LoadAndExecuteExe_SetsRegistersPerDosContract() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        context.State.DX = 0xFFFF;
        context.State.CX = 0x0000;
        context.State.BP = 0x0000;
        context.State.DI = 0xFFFF;

        using TempFile tempFile = new("DosProcessManagerTests");
        string exeFilePath = CreateFileFromResource(tempFile, "program.exe", "exe_image_0_ffff.bin");
        DosExecParameterBlock parameterBlock = CreateParameterBlock();

        DosExecResult execResult = context.ProcessManager.LoadOrLoadAndExecute(
            exeFilePath,
            parameterBlock,
            string.Empty,
            DosExecLoadType.LoadAndExecute,
            environmentSegment: 0);

        execResult.Success.Should().BeTrue();

        ushort childPspSegment = context.CurrentPspSegment;

        context.State.DX.Should().Be(childPspSegment);
        context.State.CX.Should().Be(0x00FF);
        context.State.BP.Should().Be(0x091E);
        context.State.DI.Should().Be(0x0000);
    }

    [Fact]
    public void LoadOverlay_AppliesRelocationFactorToRelocationEntries() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        const ushort relocationTargetOffset = 0;
        const ushort relocationInitialValue = 0x0042;

        using TempFile tempFile = new("DosProcessManagerTests");
        string overlayFilePath =
            CreateFileFromResource(tempFile, "overlay.exe", "overlay_exe_image_with_relocation.bin");

        const ushort loadSegment = 0x4000;
        const ushort relocationFactor = 0x1234;

        DosExecResult execResult = context.ProcessManager.LoadOverlay(
            overlayFilePath,
            loadSegment,
            relocationFactor);

        execResult.Success.Should().BeTrue();

        uint relocatedWordAddress = MemoryUtils.ToPhysicalAddress(loadSegment, relocationTargetOffset);
        ushort relocatedWord = context.Memory.UInt16[relocatedWordAddress];
        relocatedWord.Should().Be((ushort)(relocationInitialValue + relocationFactor));
        relocatedWord.Should().NotBe((ushort)(relocationInitialValue + loadSegment));
    }

    [Fact]
    public void LoadAndExecuteExe_WithSixHundredFortyKilobyteRequirement_Succeeds() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        // 640 KB expressed in paragraphs so we mirror the FreeDOS allocation threshold.
        const ushort desiredMinimumParagraphs = (ushort)(Machine.ConventionalMemorySizeKb * 64);
        const ushort programParagraphsFromHeader = 0x001C; // One 512-byte page minus the four header paragraphs of exe_image.inc.
        const ushort requiredMinAlloc = (ushort)(desiredMinimumParagraphs - (DosProgramSegmentPrefix.PspSizeInParagraphs + programParagraphsFromHeader));

        using TempFile tempFile = new("DosProcessManagerTests");
        string exeFilePath = CreateFileFromResource(tempFile, "program.exe", "exe_image_9fd4_9fd4.bin");
        byte[] exeImage = File.ReadAllBytes(exeFilePath);
        BinaryPrimitives.ReadUInt16LittleEndian(exeImage.AsSpan(0x0A)).Should().Be(requiredMinAlloc,
            "the min extra paragraphs of exe_image_9fd4_9fd4.bin must match the 640 KB requirement");
        BinaryPrimitives.ReadUInt16LittleEndian(exeImage.AsSpan(0x0C)).Should().Be(requiredMinAlloc,
            "the max extra paragraphs of exe_image_9fd4_9fd4.bin must match the 640 KB requirement");
        DosExecParameterBlock parameterBlock = CreateParameterBlock();

        DosExecResult execResult = context.ProcessManager.LoadOrLoadAndExecute(
            exeFilePath,
            parameterBlock,
            string.Empty,
            DosExecLoadType.LoadAndExecute,
            environmentSegment: 0);

        execResult.Success.Should().BeTrue("EXEC should provide {0} paragraphs for the child process", desiredMinimumParagraphs);
    }

    private static DosProcessManagerTestContext CreateContext(ushort? programEntryPointSegment = null) {
        ILogger loggerService = Substitute.For<ILogger>();

        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        AddressReadWriteBreakpoints ioPortBreakpoints = new();
        A20Gate a20Gate = new(enabled: false);
        State state = new(CpuModel.INTEL_80386);
        Memory memory = new(memoryBreakpoints, ram, a20Gate, new RealModeMmu386(), true);
        Stack stack = new(memory, state);
        IOPortDispatcher ioPortDispatcher = new(ioPortBreakpoints, state, loggerService, false);

        Configuration configuration = programEntryPointSegment.HasValue
            ? new Configuration { ProgramEntryPointSegment = programEntryPointSegment.Value, HttpApiPort = 0 }
            : new Configuration { HttpApiPort = 0 };

        // Calculate initial PSP segment and set it in the SDA
        ushort initialPspSegment = (ushort)(configuration.ProgramEntryPointSegment - 0x10);
        DosSwappableDataArea sda = new(memory, MemoryUtils.ToPhysicalAddress(DosSwappableDataArea.BaseSegment, 0));
        sda.CurrentProgramSegmentPrefix = initialPspSegment;
        BiosDataArea biosDataArea = new(memory, 640);
        InterruptVectorTable interruptVectorTable = new(memory);
        VgaRom vgaRom = new(loggerService);
        VgaFunctionality vgaFunctionality = new(memory, interruptVectorTable, ioPortDispatcher, biosDataArea, vgaRom, new VideoState(), true);

        DosDriveManager driveManager = DosTestHelpers.CreateDriveManager(loggerService, null);
        DosSysVars dosSysVars = DosTestHelpers.CreateDosSysVars(memory, loggerService,
            configuration.ProgramEntryPointSegment);
        DosMemoryManager memoryManager = new(memory, initialPspSegment, loggerService, dosSysVars, false, false);
        DosTables dosTables = new(memory);
        DosCodePageState dosCodePageState = DosCodePageState.Create(850, CountryId.UnitedStates);
        DosFileManager fileManager = new(memory, new DosStringDecoder(memory, state, dosCodePageState), driveManager, loggerService, new List<IVirtualDevice>());
        IBatchDisplayCommandHandler batchDisplayCommandHandler = new DosBatchDisplayCommandHandler(vgaFunctionality);
        Mscdex mscdex = new(state, memory, loggerService, new DriveActivityNotifier());
        DosDriveStatusProvider driveStatusProvider = new(driveManager, mscdex);

        ISoundChannelCreator channelCreator = Substitute.For<ISoundChannelCreator>();
        channelCreator.AddChannel(Arg.Any<Action<int>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<HashSet<ChannelFeature>>())
            .Returns(callInfo => {
                if (callInfo[0] is not Action<int> handler ||
                    callInfo[2] is not string name ||
                    callInfo[3] is not HashSet<ChannelFeature> features) {
                    throw new InvalidOperationException("CD audio channel registration arguments were invalid.");
                }

                return new SoundChannel(handler, name, features);
            });
        IDriveActivityNotifier activityNotifier = Substitute.For<IDriveActivityNotifier>();

        DosProcessManager processManager = new(
            memory,
            stack,
            state,
            memoryManager,
            dosTables,
            fileManager,
            driveManager,
            driveStatusProvider,
            mscdex,
            channelCreator,
            activityNotifier,
            batchDisplayCommandHandler,
            new Dictionary<string, string>(),
            loggerService);

        return new DosProcessManagerTestContext(memory, processManager, state, memoryManager);
    }

    private static DosProgramSegmentPrefix GetRootPsp(DosProcessManagerTestContext context) {
        return new DosProgramSegmentPrefix(context.Memory, MemoryUtils.ToPhysicalAddress(DosProcessManager.CommandComSegment, 0));
    }

    private static DosExecParameterBlock CreateParameterBlock() {
        ByteArrayBasedIndexable buffer = new(new byte[DosExecParameterBlock.Size]);
        return new DosExecParameterBlock(buffer.ReaderWriter, 0);
    }

    private static string CreateFileFromResource(TempFile tempFile, string targetFileName, string resourceFileName) {
        byte[] content = File.ReadAllBytes(Path.Join(AppContext.BaseDirectory, "Resources", ResourceDirectory, resourceFileName));
        return tempFile.CreateFile(targetFileName, content);
    }

    private static byte[] BuildLargeEnvironmentBytes(int totalLength) {
        if (totalLength < 2) {
            totalLength = 2;
        }

        byte[] buffer = new byte[totalLength];
        int offset = 0;
        int variableIndex = 0;

        while (offset + 2 < totalLength - 2) {
            string entry = $"VAR{variableIndex}=VALUE{variableIndex}";
            byte[] entryBytes = Encoding.ASCII.GetBytes(entry);
            if (offset + entryBytes.Length + 1 > totalLength - 2) {
                break;
            }
            Array.Copy(entryBytes, 0, buffer, offset, entryBytes.Length);
            offset += entryBytes.Length;
            buffer[offset++] = 0;
            variableIndex++;
        }

        while (offset < totalLength - 2) {
            buffer[offset++] = (byte)'A';
        }

        buffer[totalLength - 2] = 0;
        buffer[totalLength - 1] = 0;
        return buffer;
    }

    private static EnvironmentBlockInfo ReadEnvironmentBlockInfo(Memory memory, ushort environmentSegment) {
        if (environmentSegment == 0) {
            return new EnvironmentBlockInfo(0, 0, string.Empty);
        }

        uint baseAddress = MemoryUtils.ToPhysicalAddress(environmentSegment, 0);
        int offset = 0;
        int guardLimit = DosProcessManager.MaximumEnvironmentScanLength * 2;
        bool doubleNullFound = false;

        while (offset + 1 < guardLimit) {
            byte current = memory.UInt8[baseAddress + (uint)offset];
            byte next = memory.UInt8[baseAddress + (uint)(offset + 1)];
            offset++;
            if (current == 0 && next == 0) {
                offset++;
                doubleNullFound = true;
                break;
            }
        }

        if (!doubleNullFound) {
            return new EnvironmentBlockInfo(offset, 0, string.Empty);
        }

        ushort additionalStringCount = memory.UInt16[baseAddress + (uint)offset];
        offset += 2;

        List<byte> pathBytes = new();
        while (offset < guardLimit) {
            byte value = memory.UInt8[baseAddress + (uint)offset];
            offset++;
            if (value == 0) {
                break;
            }
            pathBytes.Add(value);
        }

        string programPath = Encoding.ASCII.GetString(pathBytes.ToArray());
        return new EnvironmentBlockInfo(offset, additionalStringCount, programPath);
    }

    private sealed record EnvironmentBlockInfo(int TotalLength, ushort AdditionalStringCount, string ProgramPath);

    private sealed class DosProcessManagerTestContext {
        public DosProcessManagerTestContext(Memory memory, DosProcessManager processManager,
            State state, DosMemoryManager memoryManager) {
            Memory = memory;
            ProcessManager = processManager;
            State = state;
            MemoryManager = memoryManager;
        }

        public Memory Memory { get; }
        public DosProcessManager ProcessManager { get; }
        public State State { get; }
        public DosMemoryManager MemoryManager { get; }

        /// <summary>
        /// Gets the current PSP segment directly from the DOS SDA.
        /// </summary>
        public ushort CurrentPspSegment =>
            new DosSwappableDataArea(Memory, MemoryUtils.ToPhysicalAddress(DosSwappableDataArea.BaseSegment, 0)).CurrentProgramSegmentPrefix;
    }
}
