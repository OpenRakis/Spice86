namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Dedicated unit tests for <see cref="Spice86.Core.Emulator.CPU.CfgCpu.Feeder.CfgNodeIndex"/>:
/// the final-signature lookup tiebreak and the identity-based replacement used by signature-reducer
/// fan-out.
/// </summary>
public sealed class CfgNodeIndexTest : SpeculativeTestBase {
    /// <summary>
    /// When an observed and a speculative node share an address and final signature, the lookup must
    /// return the observed one even if the speculative node was indexed first.
    /// </summary>
    [Fact]
    public void GetAtAddressMatchingFinalSignaturePrefersObservedOverSpeculative() {
        SegmentedAddress address = new(0, 0x100);
        // Index the speculative variant first so a naive "first match wins" would pick it.
        CreateSpeculativeNode(address);
        CfgInstruction observed = CreateObservedNode(address);

        CfgInstruction? match = NodeIndex.GetAtAddressMatchingFinalSignature(address, observed.SignatureFinal);

        match.Should().BeSameAs(observed, "an observed node must win the tiebreak over a speculative one");
    }

    /// <summary>
    /// With only a speculative node at the address, the lookup returns it.
    /// </summary>
    [Fact]
    public void GetAtAddressMatchingFinalSignatureReturnsSpeculativeWhenNoObservedMatches() {
        SegmentedAddress address = new(0, 0x200);
        CfgInstruction speculative = CreateSpeculativeNode(address);

        CfgInstruction? match = NodeIndex.GetAtAddressMatchingFinalSignature(address, speculative.SignatureFinal);

        match.Should().BeSameAs(speculative);
    }

    /// <summary>
    /// A different opcode at the address has a different final signature and must not match: this is
    /// what keeps distinct self-modified variants separate.
    /// </summary>
    [Fact]
    public void GetAtAddressMatchingFinalSignatureReturnsNullWhenNoFinalSignatureMatches() {
        SegmentedAddress nopAddress = new(0, 0x300);
        CreateObservedNode(nopAddress); // a NOP

        // Parse a RET elsewhere just to obtain a different final signature to query with.
        SegmentedAddress retAddress = new(0, 0x310);
        WriteRet(retAddress);
        CfgInstruction ret = Parser.ParseInstructionAt(retAddress);

        CfgInstruction? match = NodeIndex.GetAtAddressMatchingFinalSignature(nopAddress, ret.SignatureFinal);

        match.Should().BeNull("a RET final signature must not match the NOP indexed at that address");
    }

    /// <summary>
    /// ReplaceInstruction swaps the indexed node identity at the address: the old node is gone and the
    /// replacement is findable, mirroring signature-reducer fan-out.
    /// </summary>
    [Fact]
    public void ReplaceInstructionSwapsIndexedNodeIdentityAtAddress() {
        SegmentedAddress address = new(0, 0x400);
        CfgInstruction original = CreateObservedNode(address);
        // A distinct instance at the same address (re-parsed, not inserted).
        CfgInstruction replacement = WriteNopAndParse(address);

        NodeIndex.ReplaceInstruction(original, replacement);

        NodeIndex.GetAtAddress(address).Should().Contain(replacement);
        NodeIndex.GetAtAddress(address).Should().NotContain(original);
    }

    /// <summary>
    /// ReplaceInstruction is a no-op when the old node was never indexed.
    /// </summary>
    [Fact]
    public void ReplaceInstructionIsNoOpWhenOldNodeNotIndexed() {
        SegmentedAddress address = new(0, 0x500);
        CfgInstruction notIndexed = WriteNopAndParse(address);
        CfgInstruction replacement = WriteNopAndParse(address);

        NodeIndex.ReplaceInstruction(notIndexed, replacement);

        NodeIndex.HasAddress(address).Should().BeFalse("replacing an unindexed node must not insert anything");
    }
}
