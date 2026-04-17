namespace Spice86.Core.Emulator.OperatingSystem.Batch;

internal readonly struct ParsedChoiceArguments {
    internal ParsedChoiceArguments(bool suppressPrompt, bool caseSensitive,
        string choiceKeys, string promptText, char defaultChoice) {
        SuppressPrompt = suppressPrompt;
        CaseSensitive = caseSensitive;
        ChoiceKeys = choiceKeys;
        PromptText = promptText;
        DefaultChoice = defaultChoice;
    }

    internal bool SuppressPrompt { get; }
    internal bool CaseSensitive { get; }
    internal string ChoiceKeys { get; }
    internal string PromptText { get; }
    internal char DefaultChoice { get; }
}