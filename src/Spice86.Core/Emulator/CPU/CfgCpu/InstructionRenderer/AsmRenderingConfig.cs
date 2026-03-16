namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

using Spice86.Core.CLI;

public class AsmRenderingConfig(
    bool upperCase,
    bool explicitPointerType,
    bool prefixHexWith0X,
    bool hideJumpLength,
    ConditionalJumpStyle conditionalJumpStyle,
    bool dwordJumpOffset,
    int? mnemonicRightPadding,
    int addressRightSpaces,
    bool showDefaultSegment,
    bool showAllFlags,
    bool showAllSegmentRegisters,
    int addressAndInstructionRightPadding) {
    public bool UpperCase { get; } = upperCase;
    public bool ExplicitPointerType { get; } = explicitPointerType;
    public bool PrefixHexWith0X { get; } = prefixHexWith0X;
    public bool HideJumpLength { get; } = hideJumpLength;
    public ConditionalJumpStyle ConditionalJumpStyle { get; } = conditionalJumpStyle;
    public bool DwordJumpOffset { get; } = dwordJumpOffset;
    public int? MnemonicRightPadding { get; } = mnemonicRightPadding;
    public int AddressRightSpaces { get; } = addressRightSpaces;
    public bool ShowDefaultSegment { get; } = showDefaultSegment;
    public bool ShowAllFlags { get; } = showAllFlags;
    public bool ShowAllSegmentRegisters { get; } = showAllSegmentRegisters;
    public int AddressAndInstructionRightPadding { get; } = addressAndInstructionRightPadding;

    public static AsmRenderingConfig CreateSpice86Style() {
        return new AsmRenderingConfig(upperCase: true, explicitPointerType: true, prefixHexWith0X: true,
            hideJumpLength: false, conditionalJumpStyle: ConditionalJumpStyle.INTEL, dwordJumpOffset: false,
            mnemonicRightPadding: null, addressRightSpaces: 0, showDefaultSegment: true, showAllFlags: true,
            showAllSegmentRegisters: true, addressAndInstructionRightPadding: 42);
    }

    private static AsmRenderingConfig CreateDosBoxStyle() {
        return new AsmRenderingConfig(upperCase: false, explicitPointerType: false, prefixHexWith0X: false,
            hideJumpLength: true, conditionalJumpStyle: ConditionalJumpStyle.GNU, dwordJumpOffset: true,
            mnemonicRightPadding: 4, addressRightSpaces: 1, showDefaultSegment: false, showAllFlags: false,
            showAllSegmentRegisters: false, addressAndInstructionRightPadding: 42);
    }

    public static AsmRenderingConfig Create(AsmRenderingStyle style) {
        return style switch {
            AsmRenderingStyle.Spice86 => CreateSpice86Style(),
            AsmRenderingStyle.DosBox => CreateDosBoxStyle(),
            _ => throw new ArgumentOutOfRangeException(paramName: nameof(style), actualValue: style, message: null)
        };
    }
}