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

    [Fact]
    public void DescendantsWalksIfElseArmsThenTryCatchBodiesInOrder() {
        ICfgNode targetA = Substitute.For<ICfgNode>();
        ICfgNode targetB = Substitute.For<ICfgNode>();
        ICfgNode targetC = Substitute.For<ICfgNode>();
        ICfgNode targetD = Substitute.For<ICfgNode>();
        StatementItem gotoA = new GotoStatement(targetA);
        StatementItem gotoB = new GotoStatement(targetB);
        StatementItem gotoC = new GotoStatement(targetC);
        StatementItem gotoD = new GotoStatement(targetD);
        StatementItem ifElse = new IfElseStatement("ZeroFlag", [gotoA], [gotoB]);
        StatementItem tryCatch = new TryCatchStatement([gotoC], "catch (CpuException cpuException)", [gotoD]);

        List<StatementItem> descendants = StatementWalker.Descendants([ifElse, tryCatch]).ToList();

        descendants.Should().Equal(ifElse, gotoA, gotoB, tryCatch, gotoC, gotoD);
    }
}
