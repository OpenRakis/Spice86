namespace Spice86.Tests;

using Spice86.Core.CLI;
using Spice86.Shared.Utils;

using System.Diagnostics;

using Xunit;

public class CommandLineParserTests {

    [Fact]
    public void TestParseHexDecBin() {
        // nothing to convert
        Assert.Throws<FormatException>(() => CommandLineParser.ParseHexDecBinInt64(""));

        // the converter is based on Convert.Int64 routine - so min/max ranges etc. don't need check
        // no negatives with binary and hex

        Assert.Equal(2748, CommandLineParser.ParseHexDecBinInt64("2748"));
        Assert.Equal(-2748, CommandLineParser.ParseHexDecBinInt64("-2748"));

        Assert.Equal(2748, CommandLineParser.ParseHexDecBinInt64("0XABC"));
        Assert.Equal(2748, CommandLineParser.ParseHexDecBinInt64("0xABC"));
        Assert.Equal(2748, CommandLineParser.ParseHexDecBinInt64("0xabc"));
        Assert.Throws<FormatException>(() => CommandLineParser.ParseHexDecBinInt64("-0xABC"));

        Assert.Equal(2748, CommandLineParser.ParseHexDecBinInt64("0b101010111100"));
        Assert.Equal(2748, CommandLineParser.ParseHexDecBinInt64("0B101010111100"));
        Assert.Throws<FormatException>(() => CommandLineParser.ParseHexDecBinInt64("-0b101010111100"));

        Assert.Equal(65535, CommandLineParser.ParseHexDecBinUInt16("65535"));
        Assert.Equal(65535, CommandLineParser.ParseHexDecBinUInt16("0xFFFF"));
        Assert.Equal(65535, CommandLineParser.ParseHexDecBinUInt16("0b1111111111111111"));

        Assert.Throws<FormatException>(() => CommandLineParser.ParseHexDecBinUInt16("165535"));
    }
}



