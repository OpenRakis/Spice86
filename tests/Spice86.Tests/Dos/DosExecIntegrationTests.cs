namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Utils;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

using Xunit;

public class DosExecIntegrationTests {
    [Fact]
    public void LoadAndExecuteExe_WithBoundInstruction_UsesConfiguredCpuModel() {
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_exec_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        string target = Path.Join(tempDir, "bound_cpu.exe");
        byte[] exeBytes = BuildBoundBannerExeImage("BOUNDOK");
        File.WriteAllBytes(target, exeBytes);

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: target,
                enablePit: true,
                recordData: false,
                maxCycles: 200000,
                installInterruptVectors: true,
                enableA20Gate: false,
                enableXms: true,
                enableEms: true,
                cDrive: tempDir
            ).Create();

            spice86.Machine.CpuState.Flags.CpuModel = CpuModel.INTEL_80286;

            spice86.ProgramExecutor.Run();

            IMemory memory = spice86.Machine.Memory;
            uint videoBase = MemoryUtils.ToPhysicalAddress(0xB800, 0);
            const int expectedLength = 7;
            StringBuilder output = new(expectedLength);

            for (int i = 0; i < expectedLength; i++) {
                byte character = memory.UInt8[videoBase + (uint)(i * 2)];
                output.Append((char)character);
            }

            output.ToString().Should().Be("BOUNDOK");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void LoadAndExecuteExe_GenericProgram_WritesExpectedBanner() {
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_exec_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        string target = Path.Join(tempDir, "exec_test.exe");
        byte[] exeBytes = BuildVideoBannerExeImage("EXELOAD", 0, ushort.MaxValue);
        File.WriteAllBytes(target, exeBytes);

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: target,
                enablePit: true,
                recordData: false,
                maxCycles: 200000,
                installInterruptVectors: true,
                enableA20Gate: false,
                enableXms: true,
                enableEms: true,
                cDrive: tempDir
            ).Create();

            spice86.Machine.CpuState.Flags.CpuModel = CpuModel.INTEL_80286;

            spice86.ProgramExecutor.Run();

            IMemory memory = spice86.Machine.Memory;
            uint videoBase = MemoryUtils.ToPhysicalAddress(0xB800, 0);
            const int expectedLength = 7;
            StringBuilder output = new(expectedLength);

            for (int i = 0; i < expectedLength; i++) {
                byte character = memory.UInt8[videoBase + (uint)(i * 2)];
                output.Append((char)character);
            }

            output.ToString().Should().Be("EXELOAD");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void LoadAndExecuteExe_WithSixHundredFortyKilobyteRequirement_WritesBanner() {
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_exec_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        const ushort desiredMinimumParagraphs = (ushort)(600 * 64);
        const ushort programParagraphsFromHeader = 0x001C;
        const ushort requiredMinAlloc = (ushort)(desiredMinimumParagraphs - (DosProgramSegmentPrefix.PspSizeInParagraphs + programParagraphsFromHeader));

        string exePath = Path.Join(tempDir, "mem600.exe");
        byte[] exeBytes = BuildVideoBannerExeImage("MEM600", requiredMinAlloc, requiredMinAlloc);
        File.WriteAllBytes(exePath, exeBytes);

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: exePath,
                enablePit: true,
                recordData: false,
                maxCycles: 250000,
                installInterruptVectors: true,
                enableA20Gate: false,
                enableXms: true,
                enableEms: true,
                cDrive: tempDir
                // Removed programEntryPointSegment - let memory manager choose optimal location
            ).Create();

            spice86.Machine.CpuState.Flags.CpuModel = CpuModel.INTEL_80386;

            spice86.ProgramExecutor.Run();

            IMemory memory = spice86.Machine.Memory;
            uint videoBase = MemoryUtils.ToPhysicalAddress(0xB800, 0);
            const int expectedLength = 6;
            StringBuilder output = new(expectedLength);

            for (int i = 0; i < expectedLength; i++) {
                byte character = memory.UInt8[videoBase + (uint)(i * 2)];
                output.Append((char)character);
            }

            output.ToString().Should().Be("MEM600");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ExecModesAndOverlays_ShouldReportSuccessViaVideoMemory() {
        string resourceDir = Path.Join(AppContext.BaseDirectory, "Resources", "DosExecIntegration");
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_exec_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        foreach (string file in new[] { "dos_exec_master.com", "child.com", "tsr_hook.com", "overlay_driver.bin" }) {
            string source = Path.Join(resourceDir, file);
            string targetName = file == "overlay_driver.bin" ? "dos_exec_master.000" : file;
            File.Copy(source, Path.Join(tempDir, targetName), overwrite: true);
        }

        string programPath = Path.Join(tempDir, "dos_exec_master.com");

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: programPath,
                enablePit: true,
                recordData: false,
                maxCycles: 300000,
                installInterruptVectors: true,
                enableA20Gate: false,
                enableXms: true,
                enableEms: true,
                cDrive: tempDir
            ).Create();

            spice86.Machine.CpuState.Flags.CpuModel = CpuModel.INTEL_80286;

            spice86.ProgramExecutor.Run();

            IMemory memory = spice86.Machine.Memory;
            uint videoBase = MemoryUtils.ToPhysicalAddress(0xB800, 0);
            const int expectedLength = 10;
            StringBuilder output = new(expectedLength);

            for (int i = 0; i < expectedLength; i++) {
                byte character = memory.UInt8[videoBase + (uint)(i * 2)];
                output.Append((char)character);
            }

            output.ToString().Should().Be("SEMJCTLOAV");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ExecLoadOnly_FromEnvName_ShouldLoadOverlayAndResume() {
        string resourceDir = Path.Join(AppContext.BaseDirectory, "Resources", "DosExecIntegration");
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_exec_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        foreach (string file in new[] { "tentacle.bin", "tentacle.000" }) {
            string source = Path.Join(resourceDir, file);
            string target = file == "tentacle.bin" ? "tentacle.exe" : file;
            File.Copy(source, Path.Join(tempDir, target), overwrite: true);
        }

        string programPath = Path.Join(tempDir, "tentacle.exe");

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: programPath,
                enablePit: true,
                recordData: false,
                maxCycles: 200000,
                installInterruptVectors: true,
                enableA20Gate: false,
                enableXms: true,
                enableEms: true,
                cDrive: tempDir
            ).Create();

            spice86.ProgramExecutor.Run();

            IMemory memory = spice86.Machine.Memory;
            uint videoBase = MemoryUtils.ToPhysicalAddress(0xB800, 0);
            byte character = memory.UInt8[videoBase];
            ((char)character).Should().Be('K');
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ExecTheSummonning_AndRuns() {
        string resourceDir = Path.Join(AppContext.BaseDirectory, "Resources", "DosExecIntegration");
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_exec_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        string source = Path.Join(resourceDir, "the_summonning.zip");
        ZipFile.ExtractToDirectory(source, tempDir, overwriteFiles: true);
        string programPath = Path.Join(tempDir, "SUMMON.COM");

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: programPath,
                enablePit: true,
                recordData: false,
                maxCycles: 500000, // The Summoning needs more cycles than simple tests as it loads multiple EXE files (CODE.1, CODE.2)
                installInterruptVectors: true,
                enableA20Gate: false,
                enableXms: true,
                enableEms: true,
                cDrive: tempDir
            ).Create();

            // The game is not supposed to terminate - just verify it loads and runs without crashing
            // We expect it to hit the cycle limit, which is success
            try {
                spice86.ProgramExecutor.Run();
                // If we get here without exception, the game terminated normally (unexpected but ok)
            } catch (InvalidVMOperationException ex) when (ex.Message.Contains("Test ran for")) {
                // Expected - the game hit the cycle limit, which means it's running successfully
                // This is the success case for a non-terminating game
            }
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    // Lands of Lore test disabled - crashes after mouse interaction (player selection)
    // TODO: Re-enable when mouse interaction is properly handled in headless mode
    // [Fact]
    // public void ExecLandsOfLore_AndRuns() { ... }

    [Fact]
    public void ExecLoadOnly_FromSameImage_ShouldResumeAfterLoad() {
        string resourceDir = Path.Join(AppContext.BaseDirectory, "Resources", "DosExecIntegration");
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_exec_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        string source = Path.Join(resourceDir, "selfload.bin");
        string target = Path.Join(tempDir, "selfload.exe");
        File.Copy(source, target, overwrite: true);

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: target,
                enablePit: true,
                recordData: false,
                maxCycles: 200000,
                installInterruptVectors: true,
                enableA20Gate: false,
                enableXms: true,
                enableEms: true,
                cDrive: tempDir
            ).Create();

            spice86.ProgramExecutor.Run();

            IMemory memory = spice86.Machine.Memory;
            uint videoBase = MemoryUtils.ToPhysicalAddress(0xB800, 0);
            byte character = memory.UInt8[videoBase];
            ((char)character).Should().Be('O');
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    private static void TryDeleteDirectory(string directoryPath) {
        if (!Directory.Exists(directoryPath)) {
            return;
        }
        Directory.Delete(directoryPath, true);
    }

    private static byte[] BuildVideoBannerExeImage(string bannerText, ushort minExtraParagraphs, ushort maxExtraParagraphs) {
        if (string.IsNullOrEmpty(bannerText)) {
            throw new ArgumentException("Banner text must not be empty.", nameof(bannerText));
        }

        byte[] bannerBytes = Encoding.ASCII.GetBytes(bannerText);
        byte[] image = new byte[512];
        const ushort headerParagraphs = 4;
        int programStart = headerParagraphs * 16;

        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        int totalProgramLength = 0;
        WriteUInt16LittleEndian(image, 0x06, 0);
        WriteUInt16LittleEndian(image, 0x08, headerParagraphs);
        WriteUInt16LittleEndian(image, 0x0A, minExtraParagraphs);
        WriteUInt16LittleEndian(image, 0x0C, maxExtraParagraphs);
        WriteUInt16LittleEndian(image, 0x0E, 0);
        WriteUInt16LittleEndian(image, 0x10, 0xFFFE);
        WriteUInt16LittleEndian(image, 0x12, 0);
        WriteUInt16LittleEndian(image, 0x14, 0);
        WriteUInt16LittleEndian(image, 0x16, 0);
        WriteUInt16LittleEndian(image, 0x18, (ushort)programStart);
        WriteUInt16LittleEndian(image, 0x1A, 0);

        List<byte> program =
        [
            0x0E, // push cs
            0x1F, // pop ds
            .. new byte[] { 0xB8, 0x00, 0xB8 }, // mov ax, 0xB800
            .. new byte[] { 0x8E, 0xC0 }, // mov es, ax
            .. new byte[] { 0x33, 0xFF }, // xor di, di
            0xBE, // mov si, imm16
        ];
        int siImmediateIndex = program.Count;
        program.Add(0x00);
        program.Add(0x00);
        program.Add(0xB9); // mov cx, imm16
        int cxImmediateIndex = program.Count;
        program.Add(0x00);
        program.Add(0x00);
        int loopStartIndex = program.Count;
        program.Add(0xAC); // lodsb
        program.AddRange(new byte[] { 0xB4, 0x1F }); // mov ah, 0x1F
        program.Add(0xAB); // stosw
        program.Add(0xE2); // loop rel8
        int loopRelIndex = program.Count;
        program.Add(0x00);
        program.AddRange(new byte[] { 0xB8, 0x00, 0x4C, 0xCD, 0x21 }); // mov ax, 0x4C00; int 21h

        // Message offset is relative to where the program loads in memory (not the file offset)
        // The program loads at offset 0 in the load segment (header is not loaded into memory)
        ushort messageOffset = (ushort)program.Count;
        program[siImmediateIndex] = (byte)(messageOffset & 0xFF);
        program[siImmediateIndex + 1] = (byte)(messageOffset >> 8);

        ushort bannerLength = (ushort)bannerBytes.Length;
        program[cxImmediateIndex] = (byte)(bannerLength & 0xFF);
        program[cxImmediateIndex + 1] = (byte)(bannerLength >> 8);

        int relative = loopStartIndex - (loopRelIndex + 1);
        if (relative < sbyte.MinValue || relative > sbyte.MaxValue) {
            throw new InvalidOperationException("Loop range exceeded relative jump capacity.");
        }
        program[loopRelIndex] = unchecked((byte)(sbyte)relative);

        byte[] programBytes = program.ToArray();
        totalProgramLength = programBytes.Length + bannerBytes.Length;
        if (totalProgramLength > image.Length - programStart) {
            throw new InvalidOperationException("Program payload exceeds allocated image size.");
        }

        Array.Copy(programBytes, 0, image, programStart, programBytes.Length);
        Array.Copy(bannerBytes, 0, image, programStart + programBytes.Length, bannerBytes.Length);

        int fileSize = programStart + totalProgramLength;
        ushort lastPageSize = (ushort)(fileSize % 512);
        ushort pageCount = (ushort)((fileSize + 511) / 512);

        WriteUInt16LittleEndian(image, 0x02, lastPageSize);
        WriteUInt16LittleEndian(image, 0x04, pageCount);

        return image;
    }

    private static byte[] BuildBoundBannerExeImage(string bannerText) {
        byte[] bannerBytes = Encoding.ASCII.GetBytes(bannerText);
        byte[] image = new byte[512];
        const ushort headerParagraphs = 4;
        int programStart = headerParagraphs * 16;

        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        int totalProgramLength = 0;

        WriteUInt16LittleEndian(image, 0x08, headerParagraphs);
        WriteUInt16LittleEndian(image, 0x0A, 0);
        WriteUInt16LittleEndian(image, 0x0C, ushort.MaxValue);
        WriteUInt16LittleEndian(image, 0x0E, 0);
        WriteUInt16LittleEndian(image, 0x10, 0xFFFE);
        WriteUInt16LittleEndian(image, 0x12, 0);
        WriteUInt16LittleEndian(image, 0x14, 0);
        WriteUInt16LittleEndian(image, 0x16, 0);
        WriteUInt16LittleEndian(image, 0x18, (ushort)programStart);
        WriteUInt16LittleEndian(image, 0x1A, 0);

        List<byte> program = new();
        program.Add(0x0E); // push cs
        program.Add(0x1F); // pop ds
        program.Add(0xBB); // mov bx, imm16
        program.Add(0x01);
        program.Add(0x00);
        program.Add(0xBE); // mov si, imm16 (bounds)
        int siBoundsImmediateIndex = program.Count;
        program.Add(0x00);
        program.Add(0x00);
        program.Add(0x62); // bound bx, [si]
        program.Add(0x1C);
        program.AddRange(new byte[] { 0xB8, 0x00, 0xB8 }); // mov ax, 0xB800
        program.AddRange(new byte[] { 0x8E, 0xC0 }); // mov es, ax
        program.AddRange(new byte[] { 0x33, 0xFF }); // xor di, di
        program.Add(0xBE); // mov si, imm16 (message)
        int siMessageImmediateIndex = program.Count;
        program.Add(0x00);
        program.Add(0x00);
        program.Add(0xB9); // mov cx, imm16
        int cxImmediateIndex = program.Count;
        program.Add(0x00);
        program.Add(0x00);
        int loopStartIndex = program.Count;
        program.Add(0xAC); // lodsb
        program.AddRange(new byte[] { 0xB4, 0x1F }); // mov ah, 0x1F
        program.Add(0xAB); // stosw
        program.Add(0xE2); // loop rel8
        int loopRelIndex = program.Count;
        program.Add(0x00);
        program.AddRange(new byte[] { 0xB8, 0x00, 0x4C, 0xCD, 0x21 }); // mov ax, 0x4C00; int 21h

        // Bounds offset is relative to where the program loads in memory (not the file offset)
        // The program loads at offset 0 in the load segment (header is not loaded into memory)
        ushort boundsOffset = (ushort)program.Count;
        program[siBoundsImmediateIndex] = (byte)(boundsOffset & 0xFF);
        program[siBoundsImmediateIndex + 1] = (byte)(boundsOffset >> 8);

        program.Add(0x00);
        program.Add(0x00);
        program.Add((byte)bannerBytes.Length);
        program.Add(0x00);

        // Message offset is also relative to memory, not file
        ushort messageOffset = (ushort)program.Count;
        program[siMessageImmediateIndex] = (byte)(messageOffset & 0xFF);
        program[siMessageImmediateIndex + 1] = (byte)(messageOffset >> 8);

        ushort bannerLength = (ushort)bannerBytes.Length;
        program[cxImmediateIndex] = (byte)(bannerLength & 0xFF);
        program[cxImmediateIndex + 1] = (byte)(bannerLength >> 8);

        int relative = loopStartIndex - (loopRelIndex + 1);
        if (relative < sbyte.MinValue || relative > sbyte.MaxValue) {
            throw new InvalidOperationException("Loop range exceeded relative jump capacity.");
        }
        program[loopRelIndex] = unchecked((byte)(sbyte)relative);

        byte[] programBytes = program.ToArray();
        totalProgramLength = programBytes.Length + bannerBytes.Length;
        if (totalProgramLength > image.Length - programStart) {
            throw new InvalidOperationException("Program payload exceeds allocated image size.");
        }

        Array.Copy(programBytes, 0, image, programStart, programBytes.Length);
        Array.Copy(bannerBytes, 0, image, programStart + programBytes.Length, bannerBytes.Length);

        int fileSize = programStart + totalProgramLength;
        ushort lastPageSize = (ushort)(fileSize % 512);
        ushort pageCount = (ushort)((fileSize + 511) / 512);

        WriteUInt16LittleEndian(image, 0x02, lastPageSize);
        WriteUInt16LittleEndian(image, 0x04, pageCount);

        return image;
    }

    private static void WriteUInt16LittleEndian(byte[] buffer, int offset, ushort value) {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)(value >> 8);
    }
}
