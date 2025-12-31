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
using Xunit;

public class DosProcessManagerTests {
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
