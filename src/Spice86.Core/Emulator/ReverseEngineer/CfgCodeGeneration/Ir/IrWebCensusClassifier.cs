namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;

/// <summary>
/// The web census classification: sorts the webs of one method's IR SSA into
/// <see cref="WebCensus"/> kinds. Only the webs of the general registers except ESP and of ES and DS
/// are counted, and only when some version of the web has a live use. A web enters from the register
/// when a version of it is a method entry version; it leaves to the register when a live use of a
/// version is by an exit terminator or a mid-block entry statement.
/// </summary>
internal static class IrWebCensusClassifier {
    /// <summary>The census of <paramref name="webs"/>; <paramref name="midBlockEntries"/> holds the
    /// partition entry nodes that are not the first node of their block.</summary>
    public static WebCensus Classify(IrSsa ssa, IrSsaWebs webs, IReadOnlySet<ICfgNode> midBlockEntries) {
        WebCensus census = new();
        for (int web = 0; web < webs.WebCount; web++) {
            if (!Counted(webs.ValueOf(web))) {
                continue;
            }
            bool enters = false;
            bool leaves = false;
            bool hasLiveUse = false;
            int instructionUses = 0;
            HashSet<IrBlock> blocks = [];
            foreach (int version in webs.VersionsOf(web)) {
                IrSsaVersionInfo info = ssa.Info(version);
                enters |= info.Kind == IrSsaDefinitionKind.MethodEntry;
                blocks.Add(info.Block);
                foreach (IrSsaUse use in ssa.LiveUses(version)) {
                    hasLiveUse = true;
                    if (use.Phi is not null) {
                        continue;
                    }
                    blocks.Add(use.Block);
                    if (use.Terminator is IrExit
                        || (use.Statement is { } statement
                            && midBlockEntries.Contains(statement.Origin))) {
                        leaves = true;
                    } else {
                        instructionUses++;
                    }
                }
            }
            if (!hasLiveUse) {
                continue;
            }
            census.Add(enters, leaves, instructionUses, blocks.Count > 1);
        }
        return census;
    }

    private static bool Counted(IrSsaValue value) =>
        value is >= IrSsaValue.Eax and <= IrSsaValue.Edi and not IrSsaValue.Esp
            or IrSsaValue.Es or IrSsaValue.Ds;
}
