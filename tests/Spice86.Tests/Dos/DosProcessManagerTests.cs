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
        rootPsp.NextSegment.Should().Be((ushort)(DosProcessManager.CommandComSegment + DosProgramSegmentPrefix.PspSizeInParagraphs));
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
                environmentSegment: 0,
                context.InterruptVectorTable);

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
                environmentSegment: 0,
                context.InterruptVectorTable);

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
                environmentSegment: 0,
                context.InterruptVectorTable);

            parentExec.Success.Should().BeTrue();
            ushort parentSegment = context.Tracker.GetCurrentPspSegment();

            context.State.SS = 0x2222;
            context.State.SP = 0x0FF0;

            parameterBlock = CreateParameterBlock();

            DosExecResult childExec = context.ProcessManager.LoadOrLoadAndExecute(
                comFilePath,
                parameterBlock,
                string.Empty,
                DosExecLoadType.LoadAndExecute,
                environmentSegment: 0,
                context.InterruptVectorTable);

            childExec.Success.Should().BeTrue();

            context.State.SS = 0x3333;
            context.State.SP = 0x0100;

            context.ProcessManager.TerminateProcess(0, DosTerminationType.Normal, context.InterruptVectorTable);

            context.State.SS.Should().Be(0x2222);
            context.State.SP.Should().Be(0x0FF0);

            DosProgramSegmentPrefix parentPsp = new(context.Memory, MemoryUtils.ToPhysicalAddress(parentSegment, 0));
            parentPsp.StackPointer.Should().Be(0x22220FF0);
        } finally {
            DeleteIfExists(comFilePath);
        }
    }

    [Fact]
    public void LoadAndExecute_TracksParentStackPointerPerChildPsp() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        context.State.SS = 0x2640;
        context.State.SP = 0x00F8;

        uint expectedStackPointer = ((uint)context.State.SS << 16) | context.State.SP;

        string comFilePath = CreateTemporaryComFile();
        try {
            DosExecParameterBlock parameterBlock = CreateParameterBlock();
            string expectedDosFileName = Path.GetFileName(comFilePath).ToUpperInvariant();

            DosExecResult execResult = context.ProcessManager.LoadOrLoadAndExecute(
                comFilePath,
                parameterBlock,
                string.Empty,
                DosExecLoadType.LoadAndExecute,
                environmentSegment: 0,
                context.InterruptVectorTable);

            execResult.Success.Should().BeTrue();

            ushort childSegment = context.Tracker.GetCurrentPspSegment();

            IReadOnlyDictionary<ushort, uint> tracker = context.ProcessManager.PendingParentStackPointers;

            tracker.Should().ContainKey(childSegment, "EXEC should track the launch stack pointer per child PSP");
            tracker[childSegment].Should().Be(expectedStackPointer);
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
                environmentSegment: 0,
                context.InterruptVectorTable);

            execResult.Success.Should().BeTrue();

            ushort tsrSegment = context.Tracker.GetCurrentPspSegment();
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

            context.ProcessManager.TerminateProcess(0, DosTerminationType.TSR, context.InterruptVectorTable);

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
                environmentSegment: 0,
                context.InterruptVectorTable);

            execResult.Success.Should().BeTrue();

            ushort tsrSegment = context.Tracker.GetCurrentPspSegment();
            DosProgramSegmentPrefix tsrPsp = new(context.Memory, MemoryUtils.ToPhysicalAddress(tsrSegment, 0));

            ushort paragraphsToKeep = (ushort)(DosProgramSegmentPrefix.PspSizeInParagraphs + 0x10);
            DosErrorCode resizeResult = context.MemoryManager.TryModifyBlock(
                tsrSegment,
                paragraphsToKeep,
                out DosMemoryControlBlock resizedBlock);

            resizeResult.Should().Be(DosErrorCode.NoError);
            context.ProcessManager.TrackResidentBlock(tsrSegment, resizedBlock);

            ushort expectedNextSegment = (ushort)(resizedBlock.DataBlockSegment + resizedBlock.Size);

            DosProgramSegmentPrefix parentPsp = GetRootPsp(context);
            parentPsp.NextSegment.Should().NotBe(expectedNextSegment, "TSR should adjust the parent's next segment only after termination");

            context.ProcessManager.TerminateProcess(0x00, DosTerminationType.TSR, context.InterruptVectorTable);

            parentPsp = GetRootPsp(context);
            parentPsp.NextSegment.Should().Be(expectedNextSegment);

            DosMemoryControlBlock residentBlock = new(context.Memory, MemoryUtils.ToPhysicalAddress((ushort)(tsrSegment - 1), 0));
            residentBlock.Size.Should().Be(paragraphsToKeep);
            residentBlock.PspSegment.Should().Be(tsrSegment);
            residentBlock.Owner.Should().Be(resizedBlock.Owner);
            tsrPsp.NextSegment.Should().Be(expectedNextSegment);
        } finally {
            DeleteIfExists(comFilePath);
        }
    }

    [Fact]
    public void LoadAndExecute_Com_ClonesEnvironmentWithinDosLimits() {
        DosProcessManagerTestContext context = CreateContext();
        context.ProcessManager.CreateRootCommandComPsp();

        DosProgramSegmentPrefix rootPsp = GetRootPsp(context);

        byte[] largeEnvironment = BuildLargeEnvironmentBytes(DosProcessManager.EnvironmentMaximumBytes - 2);
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
                environmentSegment: 0,
                context.InterruptVectorTable);

            execResult.Success.Should().BeTrue();

            ushort childSegment = context.Tracker.GetCurrentPspSegment();
            DosProgramSegmentPrefix childPsp = new(context.Memory, MemoryUtils.ToPhysicalAddress(childSegment, 0));

            EnvironmentBlockInfo environmentInfo = ReadEnvironmentBlockInfo(context.Memory, childPsp.EnvironmentTableSegment);
            environmentInfo.TotalLength.Should().BeLessThanOrEqualTo(DosProcessManager.EnvironmentMaximumBytes);
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
                environmentSegment: 0,
                context.InterruptVectorTable);

            execResult.Success.Should().BeTrue();

            ushort childPspSegment = context.Tracker.GetCurrentPspSegment();

            context.State.DX.Should().Be(childPspSegment);
            context.State.CX.Should().Be(0x00FF);
            context.State.BP.Should().Be(0x091E);
            context.State.DI.Should().Be(0x0000);
        } finally {
            DeleteIfExists(exeFilePath);
        }
    }

    private static DosProcessManagerTestContext CreateContext() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();

        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        A20Gate a20Gate = new(enabled: false);
        Memory memory = new(memoryBreakpoints, ram, a20Gate, initializeResetVector: true);
        InterruptVectorTable interruptVectorTable = new(memory);

        Configuration configuration = new() {
            ProgramEntryPointSegment = 0x1000
        };

        DosSwappableDataArea sda = new(memory, MemoryUtils.ToPhysicalAddress(DosSwappableDataArea.BaseSegment, 0));
        DosProgramSegmentPrefixTracker tracker = new(configuration, memory, sda, loggerService);
        State state = new(CpuModel.INTEL_80386);
        DosDriveManager driveManager = new(loggerService, null, null);
        DosMemoryManager memoryManager = new(memory, tracker, loggerService);
        DosFileManager fileManager = new(memory, new DosStringDecoder(memory, state), driveManager, loggerService, new List<IVirtualDevice>());

        DosProcessManager processManager = new(
            memory,
            state,
            tracker,
            memoryManager,
            fileManager,
            driveManager,
            new Dictionary<string, string>(),
            interruptVectorTable,
            loggerService);

        return new DosProcessManagerTestContext(memory, processManager, tracker, interruptVectorTable, state, memoryManager);
    }

    private static DosProgramSegmentPrefix GetRootPsp(DosProcessManagerTestContext context) {
        return new DosProgramSegmentPrefix(context.Memory, MemoryUtils.ToPhysicalAddress(DosProcessManager.CommandComSegment, 0));
    }

    private static DosExecParameterBlock CreateParameterBlock() {
        ByteArrayBasedIndexable buffer = new(new byte[DosExecParameterBlock.Size]);
        return new DosExecParameterBlock(buffer.ReaderWriter, 0);
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
        string exeFilePath = Path.Combine(Path.GetTempPath(), $"dos_proc_{Guid.NewGuid():N}.exe");
        byte[] exeBytes = BuildMinimalExeImage();
        File.WriteAllBytes(exeFilePath, exeBytes);
        return exeFilePath;
    }

    private static byte[] BuildMinimalExeImage() {
        byte[] image = new byte[512];

        static void WriteUInt16(byte[] buffer, int offset, ushort value) {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)(value >> 8);
        }

        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        WriteUInt16(image, 0x02, 0);
        WriteUInt16(image, 0x04, 1);
        WriteUInt16(image, 0x06, 0);
        WriteUInt16(image, 0x08, 4);
        WriteUInt16(image, 0x0A, 0);
        WriteUInt16(image, 0x0C, 0xFFFF);
        WriteUInt16(image, 0x0E, 0);
        WriteUInt16(image, 0x10, 0xFFFE);
        WriteUInt16(image, 0x12, 0);
        WriteUInt16(image, 0x14, 0);
        WriteUInt16(image, 0x16, 0);
        WriteUInt16(image, 0x18, 0x40);
        WriteUInt16(image, 0x1A, 0);

        byte[] program = new byte[] { 0xB8, 0x00, 0x4C, 0xCD, 0x21 };
        Array.Copy(program, 0, image, 0x40, program.Length);

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
        int guardLimit = DosProcessManager.EnvironmentMaximumBytes * 2;
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
            DosProgramSegmentPrefixTracker tracker, InterruptVectorTable interruptVectorTable, State state, DosMemoryManager memoryManager) {
            Memory = memory;
            ProcessManager = processManager;
            Tracker = tracker;
            InterruptVectorTable = interruptVectorTable;
            State = state;
            MemoryManager = memoryManager;
        }

        public Memory Memory { get; }
        public DosProcessManager ProcessManager { get; }
        public DosProgramSegmentPrefixTracker Tracker { get; }
        public InterruptVectorTable InterruptVectorTable { get; }
        public State State { get; }
        public DosMemoryManager MemoryManager { get; }
    }
}
