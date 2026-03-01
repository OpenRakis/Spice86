namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

public class MnemonicRenderer(AsmRenderingConfig config) {
    private static readonly Dictionary<InstructionOperation, string> GnuNames = new() {
        { InstructionOperation.JA_SHORT, "JNBE_SHORT" },
        { InstructionOperation.JAE_SHORT, "JNB_SHORT" },
        { InstructionOperation.JB_SHORT, "JC_SHORT" },
        { InstructionOperation.JBE_SHORT, "JNA_SHORT" },
        { InstructionOperation.JG_SHORT, "JNLE_SHORT" },
        { InstructionOperation.JGE_SHORT, "JNL_SHORT" },
        { InstructionOperation.JL_SHORT, "JNGE_SHORT" },
        { InstructionOperation.JLE_SHORT, "JNG_SHORT" },
        { InstructionOperation.JNP_SHORT, "JPO_SHORT" },
        { InstructionOperation.JP_SHORT, "JPE_SHORT" },
        { InstructionOperation.JA_NEAR, "JNBE_NEAR" },
        { InstructionOperation.JAE_NEAR, "JNB_NEAR" },
        { InstructionOperation.JB_NEAR, "JC_NEAR" },
        { InstructionOperation.JBE_NEAR, "JNA_NEAR" },
        { InstructionOperation.JG_NEAR, "JNLE_NEAR" },
        { InstructionOperation.JGE_NEAR, "JNL_NEAR" },
        { InstructionOperation.JL_NEAR, "JNGE_NEAR" },
        { InstructionOperation.JLE_NEAR, "JNG_NEAR" },
        { InstructionOperation.JNP_NEAR, "JPO_NEAR" },
        { InstructionOperation.JP_NEAR, "JPE_NEAR" },
    };

    private static readonly HashSet<InstructionOperation> MnemonicToHideLength = [
        InstructionOperation.JA_NEAR,
        InstructionOperation.JA_SHORT,
        InstructionOperation.JAE_NEAR,
        InstructionOperation.JAE_SHORT,
        InstructionOperation.JB_NEAR,
        InstructionOperation.JB_SHORT,
        InstructionOperation.JBE_NEAR,
        InstructionOperation.JBE_SHORT,
        InstructionOperation.JCXZ_NEAR,
        InstructionOperation.JCXZ_SHORT,
        InstructionOperation.JE_NEAR,
        InstructionOperation.JE_SHORT,
        InstructionOperation.JG_NEAR,
        InstructionOperation.JG_SHORT,
        InstructionOperation.JGE_NEAR,
        InstructionOperation.JGE_SHORT,
        InstructionOperation.JL_NEAR,
        InstructionOperation.JL_SHORT,
        InstructionOperation.JLE_NEAR,
        InstructionOperation.JLE_SHORT,
        InstructionOperation.JNE_NEAR,
        InstructionOperation.JNE_SHORT,
        InstructionOperation.JNO_NEAR,
        InstructionOperation.JNO_SHORT,
        InstructionOperation.JNP_NEAR,
        InstructionOperation.JNP_SHORT,
        InstructionOperation.JNS_NEAR,
        InstructionOperation.JNS_SHORT,
        InstructionOperation.JO_NEAR,
        InstructionOperation.JO_SHORT,
        InstructionOperation.JP_NEAR,
        InstructionOperation.JP_SHORT,
        InstructionOperation.JS_NEAR,
        InstructionOperation.JS_SHORT,
        InstructionOperation.RET_FAR,
        InstructionOperation.RET_NEAR,
        InstructionOperation.CALL_FAR,
        InstructionOperation.CALL_NEAR
    ];

    public string MnemonicToString(InstructionOperation operation) {
        string mnemonic = Translate(operation).ToLower();
        if (config.HideJumpLength && MnemonicToHideLength.Contains(operation)) {
            mnemonic = mnemonic.Replace("_short", "").Replace("_near", "");
        }

        string[] mnemonicSplit = mnemonic.Split("_");
        string name = ApplyPadding(mnemonicSplit[0]);
        if (mnemonicSplit.Length > 1) {
            return name + " " + mnemonicSplit[1];
        }

        return name;
    }

    private string Translate(InstructionOperation operation) {
        if (config.ConditionalJumpStyle == ConditionalJumpStyle.GNU &&
            GnuNames.TryGetValue(operation, out string? name)) {
            return name;
        }

        name = Enum.GetName(operation);
        return name ?? throw new InvalidOperationException($"Unsupported instruction operation {operation}");
    }

    private string ApplyPadding(string mnemonic) {
        return config.MnemonicRightPadding is null ? mnemonic : mnemonic.PadRight(config.MnemonicRightPadding.Value);
    }
}