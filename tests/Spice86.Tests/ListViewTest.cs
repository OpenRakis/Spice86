namespace Spice86.Tests;

using Spice86.Shared.Utils;

using Xunit;

//GetOffsetAndLength
public class ListViewTest {
    private readonly List<int> source = new();

    public ListViewTest() {
        for (int i = 0; i < 10; i++) {
            source.Add(i);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestFullList(bool fromRange) {
        IList<int> slice = fromRange ? source.GetSlice(..10) : source.GetSlice(0, 10);
        Assert.Equal(10, slice.Count);
        AssertEqualsRange(source, slice, 0, 10);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestCutListAtEnd(bool fromRange) {
        IList<int> slice = fromRange ? source.GetSlice(..9) : source.GetSlice(0, 9);
        Assert.Equal(9, slice.Count);
        AssertEqualsRange(source, slice, 0, 9);
    }


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestCutListAtStart(bool fromRange) {
        IList<int> slice = fromRange ? source.GetSlice(1..) : source.GetSlice(1, 9);
        Assert.Equal(9, slice.Count);
        AssertEqualsRange(source, slice, 1, 10);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestCutListAtStartEmbedded(bool fromRange) {
        IList<int> sliceInner = fromRange ? source.GetSlice(1..) : source.GetSlice(1, 9);
        IList<int> slice = fromRange ? sliceInner.GetSlice(1..) : sliceInner.GetSlice(1, 8);
        Assert.Equal(8, slice.Count);
        AssertEqualsRange(source, slice, 2, 10);
    }

    private void AssertEqualsRange<T>(IList<T> expected, IList<T> actual, int expectedStart, int expectedEnd) {
        for (int i = 0; i < expectedEnd - expectedStart; i++) {
            T actualItem = actual[i];
            T expectedItem = expected[i + expectedStart];
            Assert.Equal(expectedItem, actualItem);
        }
    }
}