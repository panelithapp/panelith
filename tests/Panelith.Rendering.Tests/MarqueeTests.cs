using Panelith.Rendering.Widgets;
using Xunit;

namespace Panelith.Rendering;

public sealed class MarqueeTests
{
    [Fact]
    public void ALinePausesThenSlidesAndRestarts()
    {
        Assert.Equal(0f, TextWidgetRenderer.MarqueeShift(48f, 0.4d));

        var midSlide = TextWidgetRenderer.MarqueeShift(48f, TextWidgetRenderer.MarqueePauseSeconds + 0.5d);
        Assert.Equal(24f, midSlide);

        var cycle = TextWidgetRenderer.MarqueePauseSeconds + ((48f + TextWidgetRenderer.MarqueeGap) / TextWidgetRenderer.MarqueeSpeed);
        Assert.Equal(0f, TextWidgetRenderer.MarqueeShift(48f, cycle));
        Assert.Equal(0f, TextWidgetRenderer.MarqueeShift(0f, 5d));
    }
}
