using Xunit;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

namespace Spice86.Tests;

public class DataTypeTests {
    [Fact]
    public void BoolIsDistinctFromUint32_EqualsAndOperators() {
        DataType boolType = DataType.BOOL;
        DataType uint32Type = DataType.UINT32;

        Assert.False(boolType.Equals(uint32Type));
        Assert.False(boolType == uint32Type);
        Assert.True(boolType != uint32Type);
    }
}
