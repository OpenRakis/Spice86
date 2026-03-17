namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;

using System;
using System.Collections.Generic;
using System.Text;

internal static class InternalBatchProgramBuilder {
    internal static LaunchRequest BuildPauseLaunchRequest(CommandRedirection redirection) {
        byte[] promptBytes = Encoding.ASCII.GetBytes("Press any key to continue . . .\r\n$");

        X86InstructionBuilder code = new();

        code.WriteMovAh(0x09);
        int dxPatchOffset = code.WriteMovDxWithPlaceholder();
        code.WriteInt(0x21);
        code.WriteMovAh(0x08);
        code.WriteInt(0x21);
        code.WriteMovAxSplit(0x00, 0x4C);  // Exit with code 0
        code.WriteInt(0x21);

        int promptOffset = code.Count;
        code.AddRange(promptBytes);

        ushort promptAddr = (ushort)(0x100 + promptOffset);
        code[dxPatchOffset + 1] = (byte)(promptAddr & 0xFF);
        code[dxPatchOffset + 2] = (byte)(promptAddr >> 8);

        return new InternalProgramLaunchRequest(code.ToArray(), redirection);
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
        List<byte> code = new();
        byte[] promptBytes = prompt.Length > 0 ? Encoding.ASCII.GetBytes(prompt + "$") : Array.Empty<byte>();

        int promptDxPatchOffset = -1;
        if (promptBytes.Length > 0) {
            EmitMovAh(code, 0x09);
            promptDxPatchOffset = EmitMovDxWithPlaceholder(code);
            EmitInt21(code);
        }

        int defaultJePatchOffset = -1;
        if (defaultExitCode > 0) {
            EmitMovAh(code, 0x0B);
            EmitInt21(code);
            // INT 21h / AH=0Bh: AL=FFh if key available, AL=00h if not
            code.Add(0x3C);   // CMP AL, 0x00  ; no key waiting?
            code.Add(0x00);   //   imm8 = 0x00
            defaultJePatchOffset = code.Count;
            code.Add(0x74);   // JE <use_default>  ; if no key, use default choice (patched later)
            code.Add(0x00);   //   rel8 placeholder
        }

        int readLoopOffset = code.Count;
        EmitMovAh(code, 0x08);
        EmitInt21(code);

        if (!caseSensitive) {
            code.Add(0x3C);   // CMP AL, 'a'
            code.Add(0x61);   //   imm8 = 0x61 ('a')
            code.Add(0x72);   // JB +6  ; skip if AL < 'a' (not a letter)
            code.Add(0x06);   //   rel8 = +6
            code.Add(0x3C);   // CMP AL, 'z'
            code.Add(0x7A);   //   imm8 = 0x7A ('z')
            code.Add(0x77);   // JA +2  ; skip if AL > 'z' (not lowercase)
            code.Add(0x02);   //   rel8 = +2
            code.Add(0x2C);   // SUB AL, 0x20  ; convert lowercase to uppercase
            code.Add(0x20);   //   imm8 = 0x20
        }

        int compareStartOffset = code.Count;
        int compareBlockSize = 4 * choiceKeys.Length + 2;

        for (int i = 0; i < choiceKeys.Length; i++) {
            byte keyByte = caseSensitive
                ? (byte)choiceKeys[i]
                : (byte)char.ToUpperInvariant(choiceKeys[i]);

            code.Add(0x3C);              // CMP AL, keyByte
            code.Add(keyByte);             //   imm8 = keyByte

            int jeInstructionEnd = code.Count + 2;
            int foundOffset = compareStartOffset + compareBlockSize + i * 5;
            int relativeJump = foundOffset - jeInstructionEnd;
            code.Add(0x74);                // JE <found_i>
            code.Add((byte)relativeJump);  //   rel8
        }

        int jmpInstructionEnd = code.Count + 2;
        int backJump = readLoopOffset - jmpInstructionEnd;
        code.Add(0xEB);                    // JMP SHORT readLoop
        code.Add((byte)(backJump & 0xFF)); //   rel8

        for (int i = 0; i < choiceKeys.Length; i++) {
            byte errorLevel = (byte)(i + 1);
            EmitExitWithErrorLevel(code, errorLevel);
        }

        if (defaultExitCode > 0 && defaultJePatchOffset >= 0) {
            int useDefaultOffset = code.Count;
            EmitExitWithErrorLevel(code, defaultExitCode);
            int jeInstructionEnd = defaultJePatchOffset + 2;
            code[defaultJePatchOffset + 1] = (byte)(useDefaultOffset - jeInstructionEnd);
        }

        if (promptBytes.Length > 0 && promptDxPatchOffset >= 0) {
            int promptDataOffset = code.Count;
            code.AddRange(promptBytes);
            ushort promptAddr = (ushort)(0x100 + promptDataOffset);
            code[promptDxPatchOffset + 1] = (byte)(promptAddr & 0xFF);
            code[promptDxPatchOffset + 2] = (byte)(promptAddr >> 8);
        }

        return [.. code];
    }

    private static void EmitMovAh(List<byte> code, byte value) {
        code.Add(0xB4);   // MOV AH, imm8
        code.Add(value);  //   imm8
    }

    private static int EmitMovDxWithPlaceholder(List<byte> code) {
        int patchOffset = code.Count;
        code.Add(0xBA);   // MOV DX, imm16
        code.Add(0x00);   //   imm16 lo (patched later)
        code.Add(0x00);   //   imm16 hi (patched later)
        return patchOffset;
    }

    private static void EmitInt21(List<byte> code) {
        code.Add(0xCD);   // INT imm8
        code.Add(0x21);   //   imm8 = 0x21
    }

    private static void EmitExitWithErrorLevel(List<byte> code, byte errorLevel) {
        code.Add(0xB8);        // MOV AX, imm16
        code.Add(errorLevel);  //   imm16 lo = exit code
        code.Add(0x4C);        //   imm16 hi = 0x4C (INT 21h fn: terminate with exit code in AL)
        EmitInt21(code);       // INT 21h
    }
}