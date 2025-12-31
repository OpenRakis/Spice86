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

        return new DosProcessManagerTestContext(memory, processManager, tracker, interruptVectorTable);
    }

    private static DosProgramSegmentPrefix GetRootPsp(DosProcessManagerTestContext context) {
        return new DosProgramSegmentPrefix(context.Memory, MemoryUtils.ToPhysicalAddress(DosProcessManager.CommandComSegment, 0));
    }

    private static DosExecParameterBlock CreateParameterBlock() {
        ByteArrayBasedIndexable buffer = new(new byte[DosExecParameterBlock.Size]);
        return new DosExecParameterBlock(buffer.ReaderWriter, 0);
    }

    private sealed class DosProcessManagerTestContext {
        public DosProcessManagerTestContext(Memory memory, DosProcessManager processManager,
            DosProgramSegmentPrefixTracker tracker, InterruptVectorTable interruptVectorTable) {
            Memory = memory;
            ProcessManager = processManager;
            Tracker = tracker;
            InterruptVectorTable = interruptVectorTable;
        }

        public Memory Memory { get; }
        public DosProcessManager ProcessManager { get; }
        public DosProgramSegmentPrefixTracker Tracker { get; }
        public InterruptVectorTable InterruptVectorTable { get; }
    }
}
