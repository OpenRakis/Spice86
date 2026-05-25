namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Batch;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using System;
using System.Collections.Generic;
using System.IO;

internal sealed class CommandShell
{
    private const ushort ComOrigin = 0x100;
    private const string CommandComFileName = "COMMAND.COM";
    internal const string CommandComPath = "Z:\\COMMAND.COM";
    internal const string ShellPathValue = "Z:\\";
    internal const string StartupCommandTail = "/INIT AUTOEXEC.BAT";

    private static readonly byte[] CommandComProgramBytes = BuildCommandComProgramBytes();

    private readonly DosBatchExecutionEngine _batchExecutionEngine;
    private readonly DosDriveManager _driveManager;
    private bool _launchInteractiveShellAfterStartup;
    private bool _startupSessionStarted;

    internal CommandShell(DosBatchExecutionEngine batchExecutionEngine, DosDriveManager driveManager)
    {
        _batchExecutionEngine = batchExecutionEngine;
        _driveManager = driveManager;
    }

    internal void ResetStartupSession()
    {
        _launchInteractiveShellAfterStartup = false;
        _startupSessionStarted = false;
    }

    internal void ConfigureStartupSession(string requestedProgramDosPath, string commandTail,
        bool shouldTerminateAfterStartupProgram)
    {
        EnsureCommandComInMemoryDrive();
        _launchInteractiveShellAfterStartup = !shouldTerminateAfterStartupProgram;
        _batchExecutionEngine.ConfigureStartupSession(requestedProgramDosPath, commandTail,
            shouldTerminateAfterStartupProgram);
        _startupSessionStarted = false;
    }

    internal bool ShouldEnterInteractiveShellAfterStartup()
    {
        return _launchInteractiveShellAfterStartup;
    }

    internal bool TryStartStartupSession(out LaunchRequest launchRequest)
    {
        _startupSessionStarted = true;
        return _batchExecutionEngine.TryStartNonInteractiveSession(out launchRequest);
    }

    internal bool TryContinueStartupSession(ushort lastChildReturnCode, out LaunchRequest launchRequest)
    {
        if (!_startupSessionStarted)
        {
            return TryStartStartupSession(out launchRequest);
        }

        return _batchExecutionEngine.TryContinueNonInteractiveSession(lastChildReturnCode, out launchRequest);
    }

    internal bool ApplyRedirectionForLaunch(LaunchRequest launchRequest)
    {
        return _batchExecutionEngine.ApplyRedirectionForLaunch(launchRequest);
    }

    internal void RestoreStandardHandlesAfterLaunch()
    {
        _batchExecutionEngine.RestoreStandardHandlesAfterLaunch();
    }

    internal byte[] GetProgramBytes()
    {
        return CommandComProgramBytes;
    }

    internal bool IsCommandComProgram(string programName)
    {
        return string.Equals(programName, CommandComFileName, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureCommandComInMemoryDrive()
    {
        if (!_driveManager.TryGetMemoryDrive('Z', out MemoryDrive? zDrive))
        {
            return;
        }

        zDrive.AddFile(CommandComFileName, CommandComProgramBytes);
    }

    private static byte[] BuildCommandComProgramBytes()
    {
        List<byte> program = new();

        WriteBytes(program, 0xB4, 0x09);
        int initialPromptDxIndex = program.Count + 1;
        WriteBytes(program, 0xBA, 0x00, 0x00, 0xCD, 0x21);

        int commandLoopOffset = program.Count;
        WriteBytes(program, 0xB4, 0x0A);
        int inputBufferDxIndex = program.Count + 1;
        WriteBytes(program, 0xBA, 0x00, 0x00, 0xCD, 0x21);

        int inputCountAddressIndex = program.Count + 1;
        WriteBytes(program, 0xA0, 0x00, 0x00);
        WriteBytes(program, 0x3C, 0x04);
        int invalidLengthJumpIndex = program.Count + 1;
        WriteBytes(program, 0x75, 0x00);

        int commandTextSiIndex = program.Count + 1;
        WriteBytes(program, 0xBE, 0x00, 0x00);

        AppendExitCharacterCheck(program, 'E', out int invalidEJumpIndex);
        AppendExitCharacterCheck(program, 'X', out int invalidXJumpIndex);
        AppendExitCharacterCheck(program, 'I', out int invalidIJumpIndex);
        AppendExitCharacterCheck(program, 'T', out int invalidTJumpIndex);

        WriteBytes(program, 0xB8, 0x00, 0x4C, 0xCD, 0x21);

        int repromptOffset = program.Count;
        WriteBytes(program, 0xB4, 0x09);
        int repromptDxIndex = program.Count + 1;
        WriteBytes(program, 0xBA, 0x00, 0x00, 0xCD, 0x21);
        int loopJumpIndex = program.Count + 1;
        WriteBytes(program, 0xEB, 0x00);

        int initialPromptOffset = program.Count;
        WriteAscii(program, "C:\\>");
        program.Add(0x24);

        int repromptStringOffset = program.Count;
        WriteBytes(program, 0x0D, 0x0A);
        WriteAscii(program, "C:\\>");
        program.Add(0x24);

        int inputBufferOffset = program.Count;
        program.Add(0x3F);
        program.Add(0x00);
        for (int i = 0; i < 64; i++)
        {
            program.Add(0x00);
        }

        PatchWord(program, initialPromptDxIndex, ToComAddress(initialPromptOffset));
        PatchWord(program, inputBufferDxIndex, ToComAddress(inputBufferOffset));
        PatchWord(program, inputCountAddressIndex, ToComAddress(inputBufferOffset + 1));
        PatchWord(program, commandTextSiIndex, ToComAddress(inputBufferOffset + 2));
        PatchWord(program, repromptDxIndex, ToComAddress(repromptStringOffset));

        PatchRelativeByte(program, invalidLengthJumpIndex, repromptOffset);
        PatchRelativeByte(program, invalidEJumpIndex, repromptOffset);
        PatchRelativeByte(program, invalidXJumpIndex, repromptOffset);
        PatchRelativeByte(program, invalidIJumpIndex, repromptOffset);
        PatchRelativeByte(program, invalidTJumpIndex, repromptOffset);
        PatchRelativeByte(program, loopJumpIndex, commandLoopOffset);

        return program.ToArray();
    }

    private static void AppendExitCharacterCheck(List<byte> program, char expectedCharacter,
        out int invalidCharacterJumpIndex)
    {
        WriteBytes(program, 0xAC, 0x24, 0xDF, 0x3C, (byte)expectedCharacter);
        invalidCharacterJumpIndex = program.Count + 1;
        WriteBytes(program, 0x75, 0x00);
    }

    private static void WriteAscii(List<byte> program, string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            program.Add((byte)value[i]);
        }
    }

    private static void WriteBytes(List<byte> program, params byte[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            program.Add(values[i]);
        }
    }

    private static ushort ToComAddress(int programOffset)
    {
        return (ushort)(ComOrigin + programOffset);
    }

    private static void PatchWord(List<byte> program, int lowByteIndex, ushort value)
    {
        program[lowByteIndex] = (byte)(value & 0xFF);
        program[lowByteIndex + 1] = (byte)(value >> 8);
    }

    private static void PatchRelativeByte(List<byte> program, int displacementIndex, int targetOffset)
    {
        int displacement = targetOffset - (displacementIndex + 1);
        program[displacementIndex] = unchecked((byte)(sbyte)displacement);
    }
}