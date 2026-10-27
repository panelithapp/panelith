using Panelith.Themes;
using Xunit;

namespace Panelith.Core;

public sealed class WidgetArrangementTests
{
    private static ThemeRect Box(double x, double y, double width = 10, double height = 10) => new(x, y, width, height);

    [Fact]
    public void OneWidgetLinesUpWithTheCanvas()
    {
        var canvas = new ThemeRect(0, 0, 100, 40);
        var box = Box(5, 5, 10, 10);

        Assert.Equal(0, WidgetArrangement.Align(box, canvas, Arrangement.Left).X);
        Assert.Equal(90, WidgetArrangement.Align(box, canvas, Arrangement.Right).X);
        Assert.Equal(45, WidgetArrangement.Align(box, canvas, Arrangement.Center).X);
        Assert.Equal(0, WidgetArrangement.Align(box, canvas, Arrangement.Top).Y);
        Assert.Equal(30, WidgetArrangement.Align(box, canvas, Arrangement.Bottom).Y);
        Assert.Equal(15, WidgetArrangement.Align(box, canvas, Arrangement.Middle).Y);
    }

    [Fact]
    public void SeveralWidgetsLineUpWithTheirOwnBox()
    {
        var boxes = new[] { Box(10, 4), Box(30, 8), Box(50, 1) };
        var frame = WidgetArrangement.Union(boxes);
        var aligned = boxes.Select(box => WidgetArrangement.Align(box, frame, Arrangement.Top)).ToArray();

        Assert.Equal(1, frame.Y);
        Assert.All(aligned, box => Assert.Equal(1, box.Y));
        Assert.Equal(10, aligned[0].X);
    }

    [Fact]
    public void DistributeLeavesTheOuterTwoAndSpacesTheMiddle()
    {
        var boxes = new[] { Box(0, 0, 10, 10), Box(100, 0, 10, 10), Box(40, 0, 10, 10) };

        var spaced = WidgetArrangement.Distribute(boxes, horizontal: true);

        Assert.Equal(0, spaced[0].X);
        Assert.Equal(100, spaced[1].X);
        Assert.Equal(50, spaced[2].X);
        var two = boxes.Take(2).ToArray();
        Assert.Equal(two, WidgetArrangement.Distribute(two, horizontal: true));
    }

    [Fact]
    public void MatchSizeUsesThePrimaryWidget()
    {
        var boxes = new[] { Box(0, 0, 20, 8), Box(30, 0, 4, 40) };

        var sized = WidgetArrangement.MatchSize(boxes, source: 0, width: true, height: false);

        Assert.Equal(20, sized[1].Width);
        Assert.Equal(40, sized[1].Height);
    }
}
