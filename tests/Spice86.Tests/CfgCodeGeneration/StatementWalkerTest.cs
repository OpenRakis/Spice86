namespace Spice86.Tests.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

using FluentAssertions;
using NSubstitute;
using Xunit;

public class StatementWalkerTest {
    [Fact]
    public void DescendantsYieldsEveryItemExactlyOnceAndParentsBeforeChildren() {
        ICfgNode targetNode = Substitute.For<ICfgNode>();
        StatementItem gotoItem = new GotoStatement(targetNode);
        StatementItem dispatcher = new GotoEntryDispatcherStatement();
        StatementItem block = new BlockStatement("test", [dispatcher]);
        StatementItem switchItem = new SwitchStatement("switch (x)", [
            new SwitchCase("1", [gotoItem])
        ], [block]);

        List<StatementItem> descendants = StatementWalker.Descendants([switchItem]).ToList();

        descendants.Should().Equal(switchItem, gotoItem, block, dispatcher);
        descendants.OfType<GotoStatement>().Should().ContainSingle();
    }
}
