namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Xunit;

public class DosProcessManagerTests {
    private const ushort AdditionalEnvironmentStringsCount = 1;
    [Fact]
    public void CreateRootCommandComPsp_MatchesFreeDosLayout() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        DosProgramSegmentPrefix rootPsp = GetRootPsp(context);
        rootPsp.CurrentSize.Should().Be(DosMemoryManager.LastFreeSegment);
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

        string comFilePath = Path.Combine(Path.GetTempPath(), $"dos_proc_{Guid.NewGuid():N}.com");
        try {
            File.WriteAllBytes(comFilePath, new byte[] { 0xC3 });
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
        } finally {
            if (File.Exists(comFilePath)) {
                File.Delete(comFilePath);
            }
        }
    }

    [Fact]
    public void ExecLoadOnly_DoesNotAlterParentStackPointer() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        DosProgramSegmentPrefix rootPsp = GetRootPsp(context);
        rootPsp.StackPointer = 0x12345678;

        context.State.SS = 0x3000;
        context.State.SP = 0x00FE;

        string comFilePath = CreateTemporaryComFile();
        string expectedDosFileName = Path.GetFileName(comFilePath).ToUpperInvariant();
        try {
            DosExecParameterBlock parameterBlock = CreateParameterBlock();

            DosExecResult result = context.ProcessManager.LoadOrLoadAndExecute(
                comFilePath,
                parameterBlock,
                string.Empty,
                DosExecLoadType.LoadOnly,
                environmentSegment: 0);

            result.Success.Should().BeTrue();
            rootPsp.StackPointer.Should().Be(0x12345678);
        } finally {
            DeleteIfExists(comFilePath);
        }
    }

    [Fact]
    public void TerminateProcess_RestoresParentStackPointer() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        string comFilePath = CreateTemporaryComFile();
        try {
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
            // SP is restored to the iregs frame location (parent SP minus IregsFrameSize of 18 bytes).
            // FreeDOS stores ps_stack = SS:(SP - sizeof(iregs)) during EXEC.
            context.State.SP.Should().Be((ushort)(0xFFFE - 18));
        } finally {
            DeleteIfExists(comFilePath);
        }
    }

    [Fact]
    public void TerminateProcess_TsrFreesEnvironmentBlock() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        string comFilePath = CreateTemporaryComFile(0x200);
        try {
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
        } finally {
            DeleteIfExists(comFilePath);
        }
    }

    [Fact]
    public void TerminateProcess_TsrRetainsResidentBlockMetadata() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        string comFilePath = CreateTemporaryComFile(0x200);
        try {
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
        } finally {
            DeleteIfExists(comFilePath);
        }
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

        string comFilePath = CreateTemporaryComFile();
        try {
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
        } finally {
            DeleteIfExists(comFilePath);
        }
    }

    [Fact]
    public void LoadAndExecuteExe_SetsRegistersPerDosContract() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        context.State.DX = 0xFFFF;
        context.State.CX = 0x0000;
        context.State.BP = 0x0000;
        context.State.DI = 0xFFFF;

        string exeFilePath = CreateTemporaryExeFile();
        try {
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
        } finally {
            DeleteIfExists(exeFilePath);
        }
    }

    [Fact]
    public void LoadOverlay_AppliesRelocationFactorToRelocationEntries() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        const ushort relocationTargetOffset = 0;
        const ushort relocationInitialValue = 0x0042;
        byte[] overlayBytes = BuildOverlayExeImageWithRelocation(relocationTargetOffset, relocationInitialValue);

        string overlayFilePath = Path.Combine(Path.GetTempPath(), $"dos_overlay_{Guid.NewGuid():N}.exe");
        try {
            File.WriteAllBytes(overlayFilePath, overlayBytes);

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
        } finally {
            DeleteIfExists(overlayFilePath);
        }
    }

    [Fact]
    public void LoadAndExecuteExe_WithSixHundredFortyKilobyteRequirement_Succeeds() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        // 640 KB expressed in paragraphs so we mirror the FreeDOS allocation threshold.
        const ushort desiredMinimumParagraphs = (ushort)(Machine.ConventionalMemorySizeKb * 64);
        const ushort programParagraphsFromHeader = 0x001C; // Derived from BuildExeImage layout.
        const ushort requiredMinAlloc = (ushort)(desiredMinimumParagraphs - (DosProgramSegmentPrefix.PspSizeInParagraphs + programParagraphsFromHeader));

        string exeFilePath = CreateTemporaryExeFile(requiredMinAlloc, requiredMinAlloc);
        try {
            DosExecParameterBlock parameterBlock = CreateParameterBlock();

            DosExecResult execResult = context.ProcessManager.LoadOrLoadAndExecute(
                exeFilePath,
                parameterBlock,
                string.Empty,
                DosExecLoadType.LoadAndExecute,
                environmentSegment: 0);

            execResult.Success.Should().BeTrue("EXEC should provide {0} paragraphs for the child process", desiredMinimumParagraphs);
        } finally {
            DeleteIfExists(exeFilePath);
        }
    }

    private static DosProcessManagerTestContext CreateContext(ushort? programEntryPointSegment = null) {
        ILoggerService loggerService = Substitute.For<ILoggerService>();

        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        A20Gate a20Gate = new(enabled: false);
        State state = new(CpuModel.INTEL_80386);
        Memory memory = new(memoryBreakpoints, ram, a20Gate, initializeResetVector: true);
        Stack stack = new(memory, state);

        Configuration configuration = programEntryPointSegment.HasValue
            ? new Configuration { ProgramEntryPointSegment = programEntryPointSegment.Value }
            : new Configuration();

        // Calculate initial PSP segment and set it in the SDA
        ushort initialPspSegment = (ushort)(configuration.ProgramEntryPointSegment - 0x10);
        DosSwappableDataArea sda = new(memory, MemoryUtils.ToPhysicalAddress(DosSwappableDataArea.BaseSegment, 0));
        sda.CurrentProgramSegmentPrefix = initialPspSegment;

        DosDriveManager driveManager = new(loggerService, null, null);
        DosMemoryManager memoryManager = new(memory, initialPspSegment, loggerService);
        DosFileManager fileManager = new(memory, new DosStringDecoder(memory, state), driveManager, loggerService, new List<IVirtualDevice>());

        DosProcessManager processManager = new(
            memory,
            stack,
            state,
            memoryManager,
            fileManager,
            driveManager,
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

    private static void WriteUInt16LittleEndian(byte[] buffer, int offset, ushort value) {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)(value >> 8);
    }

    private static string CreateTemporaryComFile(int payloadLength = 1) {
        if (payloadLength < 1) {
            payloadLength = 1;
        }

        string comFilePath = Path.Combine(Path.GetTempPath(), $"dos_proc_{Guid.NewGuid():N}.com");
        byte[] bytes = new byte[payloadLength];
        for (int i = 0; i < payloadLength - 1; i++) {
            bytes[i] = 0x90;
        }
        bytes[payloadLength - 1] = 0xC3;
        File.WriteAllBytes(comFilePath, bytes);
        return comFilePath;
    }

    private static string CreateTemporaryExeFile() {
        return CreateTemporaryExeFile(0, 0xFFFF);
    }

    private static string CreateTemporaryExeFile(ushort minExtraParagraphs, ushort maxExtraParagraphs) {
        string exeFilePath = Path.Combine(Path.GetTempPath(), $"dos_proc_{Guid.NewGuid():N}.exe");
        byte[] exeBytes = BuildExeImage(minExtraParagraphs, maxExtraParagraphs);
        File.WriteAllBytes(exeFilePath, exeBytes);
        return exeFilePath;
    }

    private static byte[] BuildExeImage(ushort minExtraParagraphs, ushort maxExtraParagraphs) {
        byte[] image = new byte[512];

        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        WriteUInt16LittleEndian(image, 0x02, 0);
        WriteUInt16LittleEndian(image, 0x04, 1);
        WriteUInt16LittleEndian(image, 0x06, 0);
        WriteUInt16LittleEndian(image, 0x08, 4);
        WriteUInt16LittleEndian(image, 0x0A, minExtraParagraphs);
        WriteUInt16LittleEndian(image, 0x0C, maxExtraParagraphs);
        WriteUInt16LittleEndian(image, 0x0E, 0);
        WriteUInt16LittleEndian(image, 0x10, 0xFFFE);
        WriteUInt16LittleEndian(image, 0x12, 0);
        WriteUInt16LittleEndian(image, 0x14, 0);
        WriteUInt16LittleEndian(image, 0x16, 0);
        WriteUInt16LittleEndian(image, 0x18, 0x40);
        WriteUInt16LittleEndian(image, 0x1A, 0);

        byte[] program = new byte[] { 0xB8, 0x00, 0x4C, 0xCD, 0x21 };
        Array.Copy(program, 0, image, 0x40, program.Length);

        return image;
    }

    private static byte[] BuildOverlayExeImageWithRelocation(ushort relocationTargetOffset, ushort initialWordValue) {
        const ushort headerParagraphs = 2;
        int headerSizeBytes = headerParagraphs * 16;
        byte[] image = new byte[512];

        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        WriteUInt16LittleEndian(image, 0x02, 0);
        WriteUInt16LittleEndian(image, 0x04, 1);
        WriteUInt16LittleEndian(image, 0x06, 1);
        WriteUInt16LittleEndian(image, 0x08, headerParagraphs);
        WriteUInt16LittleEndian(image, 0x0A, 0);
        WriteUInt16LittleEndian(image, 0x0C, 0);
        WriteUInt16LittleEndian(image, 0x0E, 0);
        WriteUInt16LittleEndian(image, 0x10, 0);
        WriteUInt16LittleEndian(image, 0x12, 0);
        WriteUInt16LittleEndian(image, 0x14, 0);
        WriteUInt16LittleEndian(image, 0x16, 0);
        WriteUInt16LittleEndian(image, 0x18, 0x1C);
        WriteUInt16LittleEndian(image, 0x1A, 0);

        WriteUInt16LittleEndian(image, 0x1C, relocationTargetOffset);
        WriteUInt16LittleEndian(image, 0x1E, 0);

        int wordOffset = headerSizeBytes + relocationTargetOffset;
        WriteUInt16LittleEndian(image, wordOffset, initialWordValue);

        return image;
    }

    private static void DeleteIfExists(string path) {
        if (File.Exists(path)) {
            File.Delete(path);
        }
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
