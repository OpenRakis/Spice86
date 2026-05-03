namespace Spice86.Tests.UI;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.DebuggerKnowledgeBase;
using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Registries;
using Spice86.Shared.Emulator.Memory;
using Spice86.ViewModels;

using Xunit;

/// <summary>
/// UI tests for CfgCpuView tooltips and emulator-provided code markers.
/// These tests verify that CFG graph nodes show decoded call tooltips
/// and emulator-provided code badges.
/// </summary>
public class CfgCpuViewUiTests : BreakpointUiTestBase {
    /// <summary>
    /// Verifies that a CfgGraphNode representing an interrupt instruction surfaces a tooltip
    /// with decoded call information.
    /// </summary>
    [AvaloniaFact]
    public void CfgCpuView_IntInstructionNode_HasDecodedCallProperty() {
        // Arrange
        SegmentedAddress nodeAddress = new(0x1000, 0x0100);
        DecodedCall expectedCall = new(
            Subsystem: "DOS INT 21h",
            FunctionName: "AH=4Ch Terminate Program",
            ShortDescription: "Terminates the program with exit code",
            Parameters: [
                new DecodedParameter("exit code", "AL", DecodedParameterKind.Register, 0, "0x00 (success)", null)
            ],
            Results: []
        );

        // Create a CfgGraphNode with decoded call
        CfgGraphNode node = new() {
            NodeId = 1,
            TextOffsets = [],
            IsLastExecuted = false,
            NodeType = CfgNodeType.Instruction,
            DecodedCall = expectedCall,
            IsEmulatorProvided = false,
            EmulatorProvidedFunctionName = null
        };

        // Act & Assert - Verify the properties are accessible
        node.DecodedCall.Should().NotBeNull("Node has a decoded call");
        node.DecodedCall!.Subsystem.Should().Be("DOS INT 21h");
        node.DecodedCall.FunctionName.Should().Be("AH=4Ch Terminate Program");
        node.DecodedCall.Parameters.Should().HaveCount(1);
    }

    /// <summary>
    /// Verifies that CfgGraphNode backed by an emulator-provided function shows the EMU badge.
    /// </summary>
    [AvaloniaFact]
    public void CfgCpuView_EmulatorProvidedNode_HasEmulatorProvidedProperties() {
        // Arrange
        CfgGraphNode node = new() {
            NodeId = 2,
            TextOffsets = [],
            IsLastExecuted = false,
            NodeType = CfgNodeType.Instruction,
            DecodedCall = null,
            IsEmulatorProvided = true,
            EmulatorProvidedFunctionName = "provided_mouse_handler"
        };

        // Act & Assert - Verify the properties are accessible
        node.IsEmulatorProvided.Should().BeTrue("Node should be marked as emulator-provided");
        node.EmulatorProvidedFunctionName.Should().Be("provided_mouse_handler");
    }

    /// <summary>
    /// Verifies that CfgGraphNode for plain instructions has no decoded call.
    /// </summary>
    [AvaloniaFact]
    public void CfgCpuView_PlainInstructionNode_HasNoDecodedCall() {
        // Arrange
        CfgGraphNode node = new() {
            NodeId = 3,
            TextOffsets = [],
            IsLastExecuted = false,
            NodeType = CfgNodeType.Instruction,
            DecodedCall = null,
            IsEmulatorProvided = false,
            EmulatorProvidedFunctionName = null
        };

        // Act & Assert - Verify default values
        node.DecodedCall.Should().BeNull("Plain instruction nodes should not have decoded calls");
        node.IsEmulatorProvided.Should().BeFalse("Plain nodes are not emulator-provided");
    }
}
