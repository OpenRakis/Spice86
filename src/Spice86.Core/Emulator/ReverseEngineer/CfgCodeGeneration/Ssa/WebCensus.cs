namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;

/// <summary>
/// How the register webs of generated methods would fare as C# locals: a web that enters from the
/// register would be loaded at method or block entries, one that leaves to it would be written back
/// at boundaries, and a pure web needs neither. The uses counted per kind are the web's instruction
/// uses (live uses by nodes that are neither boundaries nor mid-block entries).
/// </summary>
internal sealed class WebCensus {
    /// <summary>The number of counted webs (only webs with at least one live use).</summary>
    public int Webs { get; private set; }
    /// <summary>Webs that neither enter from nor leave to the register.</summary>
    public int PureWebs { get; private set; }
    /// <summary>Webs that enter from the register but do not leave to it.</summary>
    public int LoadOnlyWebs { get; private set; }
    /// <summary>Webs that leave to the register but do not enter from it.</summary>
    public int StoreOnlyWebs { get; private set; }
    /// <summary>Webs that both enter from and leave to the register.</summary>
    public int ThroughWebs { get; private set; }
    /// <summary>The instruction uses of the pure webs.</summary>
    public int PureUses { get; private set; }
    /// <summary>The instruction uses of the load-only webs.</summary>
    public int LoadOnlyUses { get; private set; }
    /// <summary>The instruction uses of the store-only webs.</summary>
    public int StoreOnlyUses { get; private set; }
    /// <summary>The instruction uses of the through webs.</summary>
    public int ThroughUses { get; private set; }
    /// <summary>Pure webs whose defining and using nodes span more than one block.</summary>
    public int PureMultiBlockWebs { get; private set; }

    /// <summary>Adds one web of <paramref name="uses"/> instruction uses; <paramref name="spansBlocks"/>
    /// tells whether its defining and using nodes span more than one block.</summary>
    public void Add(bool enters, bool leaves, int uses, bool spansBlocks) {
        Webs++;
        if (enters && leaves) {
            ThroughWebs++;
            ThroughUses += uses;
        } else if (enters) {
            LoadOnlyWebs++;
            LoadOnlyUses += uses;
        } else if (leaves) {
            StoreOnlyWebs++;
            StoreOnlyUses += uses;
        } else {
            PureWebs++;
            PureUses += uses;
            if (spansBlocks) {
                PureMultiBlockWebs++;
            }
        }
    }

    /// <summary>Adds every count of <paramref name="other"/> to this census.</summary>
    public void Merge(WebCensus other) {
        Webs += other.Webs;
        PureWebs += other.PureWebs;
        LoadOnlyWebs += other.LoadOnlyWebs;
        StoreOnlyWebs += other.StoreOnlyWebs;
        ThroughWebs += other.ThroughWebs;
        PureUses += other.PureUses;
        LoadOnlyUses += other.LoadOnlyUses;
        StoreOnlyUses += other.StoreOnlyUses;
        ThroughUses += other.ThroughUses;
        PureMultiBlockWebs += other.PureMultiBlockWebs;
    }
}
