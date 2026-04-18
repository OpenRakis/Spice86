namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;

using System;
using System.Text;

internal static class InternalBatchProgramBuilder {
    private const ushort ComOrigin = 0x100;

    internal static LaunchRequest BuildPauseLaunchRequest(CommandRedirection redirection) {
        byte[] promptBytes = Encoding.ASCII.GetBytes("Press any key to continue . . .\r\n$");

        byte[] buffer = new byte[1024];
        ByteArrayBasedIndexable indexable = new(buffer);
        MemoryAsmWriter writer = new(indexable, new SegmentedAddress(0, 0));

        writer.WriteMovAh(0x09);
        ushort dxPatchOffset = writer.CurrentAddress.Offset;
        writer.WriteMovDx(0x0000);
        writer.WriteInt(0x21);
        writer.WriteMovAh(0x08);
        writer.WriteInt(0x21);
        writer.WriteMovAxSplit(0x00, 0x4C);
        writer.WriteInt(0x21);

        ushort promptOffset = writer.CurrentAddress.Offset;
        writer.WriteBytes(promptBytes);

        ushort promptAddr = (ushort)(ComOrigin + promptOffset);
        buffer[dxPatchOffset + 1] = (byte)(promptAddr & 0xFF);
        buffer[dxPatchOffset + 2] = (byte)(promptAddr >> 8);

        byte[] result = new byte[writer.CurrentAddress.Offset];
        Array.Copy(buffer, result, result.Length);
        return new InternalProgramLaunchRequest(result, redirection);
    }

    internal static LaunchRequest BuildChoiceLaunchRequest(string arguments, CommandRedirection redirection) {
        ParsedChoiceArguments parsedChoiceArguments = ParseChoiceArguments(arguments);
        string prompt = BuildChoicePromptString(parsedChoiceArguments.SuppressPrompt,
            parsedChoiceArguments.ChoiceKeys, parsedChoiceArguments.PromptText);

        byte defaultExitCode = 0;
        if (parsedChoiceArguments.DefaultChoice != '\0') {
            StringComparison comparison = parsedChoiceArguments.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            for (int i = 0; i < parsedChoiceArguments.ChoiceKeys.Length; i++) {
                if (string.Equals(parsedChoiceArguments.ChoiceKeys[i].ToString(),
                    parsedChoiceArguments.DefaultChoice.ToString(), comparison)) {
                    defaultExitCode = (byte)(i + 1);
                    break;
                }
            }
        }

        byte[] comBytes = BuildChoiceComStub(parsedChoiceArguments.ChoiceKeys,
            parsedChoiceArguments.CaseSensitive, prompt, defaultExitCode);
        return new InternalProgramLaunchRequest(comBytes, redirection);
    }

    private static string BuildChoicePromptString(bool suppressPrompt, string choiceKeys, string promptText) {
        if (suppressPrompt) {
            return promptText.Length > 0 ? $"{promptText}\r\n" : string.Empty;
        }

        StringBuilder prompt = new();
        if (promptText.Length > 0) {
            prompt.Append(promptText);
        }

        prompt.Append('[');
        for (int i = 0; i < choiceKeys.Length; i++) {
            if (i > 0) {
                prompt.Append(',');
            }

            prompt.Append(choiceKeys[i]);
        }

        prompt.Append("]?\r\n");
        return prompt.ToString();
    }

    private static ParsedChoiceArguments ParseChoiceArguments(string arguments) {
        bool suppressPrompt = false;
        bool caseSensitive = false;
        string choiceKeys = "YN";
        char defaultChoice = '\0';

        StringBuilder promptBuilder = new();
        string remaining = arguments;

        while (remaining.Length > 0) {
            remaining = remaining.TrimStart();
            if (remaining.Length == 0) {
                break;
            }

            if (TryConsumeFlagSwitch(ref remaining, 'N')) {
                suppressPrompt = true;
                continue;
            }

            if (TryConsumeFlagSwitch(ref remaining, 'S')) {
                caseSensitive = true;
                continue;
            }

            if (TryConsumeSwitchArgument(ref remaining, 'C', out string consumedChoiceKeys)) {
                choiceKeys = consumedChoiceKeys;
                continue;
            }

            if (TryConsumeSwitchArgument(ref remaining, 'T', out string timeoutArgument)) {
                if (timeoutArgument.Length > 0) {
                    defaultChoice = timeoutArgument[0];
                }

                continue;
            }

            if (remaining.StartsWith("/", StringComparison.Ordinal)) {
                SkipUnknownSwitch(ref remaining);
                continue;
            }

            AppendPromptChunk(ref promptBuilder, ref remaining);
        }

        return new ParsedChoiceArguments(suppressPrompt, caseSensitive,
            choiceKeys, promptBuilder.ToString(), defaultChoice);
    }

    private static bool TryConsumeFlagSwitch(ref string remaining, char optionLetter) {
        if (!remaining.StartsWith($"/{optionLetter}", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (remaining.Length != 2 && remaining[2] != ' ' && remaining[2] != '/') {
            return false;
        }

        remaining = remaining.Substring(2);
        return true;
    }

    private static bool TryConsumeSwitchArgument(ref string remaining, char optionLetter, out string optionValue) {
        optionValue = string.Empty;

        if (!remaining.StartsWith($"/{optionLetter}", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (remaining.Length <= 2) {
            return false;
        }

        bool hasColon = remaining[2] == ':';
        if (!hasColon && (remaining[2] == ' ' || remaining[2] == '/')) {
            return false;
        }

        int valueStart = hasColon ? 3 : 2;
        remaining = remaining.Substring(valueStart);

        int end = remaining.IndexOfAny(new[] { ' ', '/' });
        if (end < 0) {
            optionValue = remaining;
            remaining = string.Empty;
            return true;
        }

        optionValue = remaining.Substring(0, end);
        remaining = remaining.Substring(end);
        return true;
    }

    private static void SkipUnknownSwitch(ref string remaining) {
        int end = remaining.IndexOfAny(new[] { ' ', '/' }, 1);
        remaining = end < 0 ? string.Empty : remaining.Substring(end);
    }

    private static void AppendPromptChunk(ref StringBuilder promptBuilder, ref string remaining) {
        int nextSlash = remaining.IndexOf('/');
        if (nextSlash < 0) {
            if (promptBuilder.Length > 0) {
                promptBuilder.Append(' ');
            }

            promptBuilder.Append(remaining);
            remaining = string.Empty;
            return;
        }

        string textChunk = remaining.Substring(0, nextSlash).TrimEnd();
        if (textChunk.Length > 0) {
            if (promptBuilder.Length > 0) {
                promptBuilder.Append(' ');
            }

            promptBuilder.Append(textChunk);
        }

        remaining = remaining.Substring(nextSlash);
    }

    private static byte[] BuildChoiceComStub(string choiceKeys, bool caseSensitive, string prompt,
        byte defaultExitCode) {
        byte[] buffer = new byte[4096];
        ByteArrayBasedIndexable indexable = new(buffer);
        MemoryAsmWriter writer = new(indexable, new SegmentedAddress(0, 0));

        byte[] promptBytes = prompt.Length > 0 ? Encoding.ASCII.GetBytes(prompt + "$") : Array.Empty<byte>();

        int promptDxPatchOffset = -1;
        if (promptBytes.Length > 0) {
            writer.WriteMovAh(0x09);
            promptDxPatchOffset = writer.CurrentAddress.Offset;
            writer.WriteMovDx(0x0000);
            writer.WriteInt(0x21);
        }

        int defaultJePatchOffset = -1;
        if (defaultExitCode > 0) {
            writer.WriteMovAh(0x0B);
            writer.WriteInt(0x21);
            writer.WriteCmpAl(0x00);
            defaultJePatchOffset = writer.CurrentAddress.Offset;
            writer.WriteJz(0);
        }

        int readLoopOffset = writer.CurrentAddress.Offset;
        writer.WriteMovAh(0x08);
        writer.WriteInt(0x21);

        if (!caseSensitive) {
            writer.WriteCmpAl(0x61);
            writer.WriteJb(6);
            writer.WriteCmpAl(0x7A);
            writer.WriteJa(2);
            writer.WriteSubAl(0x20);
        }

        int compareStartOffset = writer.CurrentAddress.Offset;
        int compareBlockSize = 4 * choiceKeys.Length + 2;

        for (int i = 0; i < choiceKeys.Length; i++) {
            byte keyByte = caseSensitive
                ? (byte)choiceKeys[i]
                : (byte)char.ToUpperInvariant(choiceKeys[i]);

            writer.WriteCmpAl(keyByte);

            int jeInstructionEnd = writer.CurrentAddress.Offset + 2;
            int foundOffset = compareStartOffset + compareBlockSize + i * 5;
            int relativeJump = foundOffset - jeInstructionEnd;
            writer.WriteJz((sbyte)relativeJump);
        }

        int jmpInstructionEnd = writer.CurrentAddress.Offset + 2;
        int backJump = readLoopOffset - jmpInstructionEnd;
        writer.WriteJumpShort((sbyte)(backJump & 0xFF));

        for (int i = 0; i < choiceKeys.Length; i++) {
            byte errorLevel = (byte)(i + 1);
            writer.WriteMovAxSplit(errorLevel, 0x4C);
            writer.WriteInt(0x21);
        }

        if (defaultExitCode > 0 && defaultJePatchOffset >= 0) {
            int useDefaultOffset = writer.CurrentAddress.Offset;
            writer.WriteMovAxSplit(defaultExitCode, 0x4C);
            writer.WriteInt(0x21);
            int jeInstructionEnd = defaultJePatchOffset + 2;
            buffer[defaultJePatchOffset + 1] = (byte)(useDefaultOffset - jeInstructionEnd);
        }

        if (promptBytes.Length > 0 && promptDxPatchOffset >= 0) {
            int promptDataOffset = writer.CurrentAddress.Offset;
            writer.WriteBytes(promptBytes);
            ushort promptAddr = (ushort)(ComOrigin + promptDataOffset);
            buffer[promptDxPatchOffset + 1] = (byte)(promptAddr & 0xFF);
            buffer[promptDxPatchOffset + 2] = (byte)(promptAddr >> 8);
        }

        byte[] result = new byte[writer.CurrentAddress.Offset];
        Array.Copy(buffer, result, result.Length);
        return result;
    }
}