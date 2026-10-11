namespace StartBarController.Core.Tests;

public class StuckRectsTests
{
    [Fact]
    public void ReadsEdgeFromByte12()
    {
        Assert.Equal(TaskbarEdge.Bottom, StuckRects.ReadEdge(Samples.StuckRectsBottom()));
    }

    [Theory]
    [InlineData(TaskbarEdge.Left)]
    [InlineData(TaskbarEdge.Top)]
    [InlineData(TaskbarEdge.Right)]
    [InlineData(TaskbarEdge.Bottom)]
    public void WithEdgeChangesOnlyTheEdgeByte(TaskbarEdge edge)
    {
        byte[] original = Samples.StuckRectsBottom();
        byte[] updated = StuckRects.WithEdge(original, edge);

        Assert.Equal(edge, StuckRects.ReadEdge(updated));
        Assert.Equal((byte)edge, updated[12]);
        for (int i = 0; i < original.Length; i++)
        {
            if (i != 12)
            {
                Assert.Equal(original[i], updated[i]);
            }
        }

        Assert.Equal(TaskbarEdge.Bottom, StuckRects.ReadEdge(original)); // input untouched
    }

    [Fact]
    public void ReadEdgeRejectsShortOrUnknownData()
    {
        Assert.Null(StuckRects.ReadEdge(null));
        Assert.Null(StuckRects.ReadEdge(new byte[12]));
        byte[] blob = Samples.StuckRectsBottom();
        blob[12] = 9;
        Assert.Null(StuckRects.ReadEdge(blob));
    }

    [Fact]
    public void WithEdgeRejectsShortData()
    {
        Assert.Throws<ArgumentException>(() => StuckRects.WithEdge(new byte[5], TaskbarEdge.Top));
    }
}
