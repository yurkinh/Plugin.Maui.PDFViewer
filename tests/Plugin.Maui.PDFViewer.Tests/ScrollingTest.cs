using FluentAssertions;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Plugin.Maui.PDFViewer.Tests;

public class ScrollingTest
{
    [Fact]
    public void AFingerLiftedWhileMovingHasItsRecentVelocity()
    {
        var sut = new FlingTracker();
        sut.Add(0, 0, 0);
        sut.Add(50, 0, -20);   // slow start, outside the last 100 ms by the end
        sut.Add(200, 0, -100);
        sut.Add(250, 0, -200);
        sut.Add(300, 0, -300);

        sut.Velocity(nowMs: 310).Should().Be((0, -2), "200 units up over the last 100 ms");
    }

    [Fact]
    public void AFingerThatRestedBeforeLiftingDoesNotFling()
    {
        var sut = new FlingTracker();
        sut.Add(0, 0, 0);
        sut.Add(50, 0, -100);

        var velocity = sut.Velocity(nowMs: 50 + FlingTracker.RestMs + 1);

        velocity.Should().Be((0, 0));
        Fling.ShouldStart(velocity).Should().BeFalse();
    }

    [Fact]
    public void AFlingSlowsDownAndCoversAFiniteDistance()
    {
        (double X, double Y) v = (0, -2); // 2 units per ms upwards
        double travelled = 0;
        var frames = 0;
        while (Fling.IsMoving(v) && frames < 10_000)
        {
            travelled += Fling.Distance(v, 16).Y;
            v = Fling.Decay(v, 16);
            frames++;
        }

        travelled.Should().BeApproximately(-2 * Fling.TimeConstantMs, 2 * Fling.MinSpeed * Fling.TimeConstantMs,
            "an exponential fling travels v0 x the time constant");
        (frames * 16).Should().BeInRange(1200, 1700, "it settles in about 1.5 s: ln(2 / 0.02) time constants");
    }

    [Fact]
    public void AFlingIsCappedAndNeedsAMinimumSpeed()
    {
        Fling.Clamp((-50, 3)).Should().Be((-Fling.MaxSpeed, 3));
        Fling.ShouldStart((0.1, 0.1)).Should().BeFalse();
        Fling.ShouldStart((0, 0.3)).Should().BeTrue();
    }

    [Fact]
    public void TheVerticalThumbShowsTheShareInViewAndThePosition()
    {
        // A 400 x 1000 view over content 4000 high: a quarter in view, scrolled half way.
        var state = new ScrollState(0, 1500, 0, 3000, 400, 4000);

        var thumb = ScrollIndicator.Vertical(state, 400, 1000)!.Value;

        var track = 1000 - 2 * ScrollIndicator.Inset;
        thumb.Height.Should().BeApproximately(track / 4, 0.01f);
        thumb.Y.Should().BeApproximately(ScrollIndicator.Inset + (track - track / 4) / 2, 0.01f);
        thumb.X.Should().Be(400 - ScrollIndicator.Inset - ScrollIndicator.Thickness, "at the right edge");
        ScrollIndicator.Horizontal(state, 400, 1000).Should().BeNull("nothing scrolls sideways");
    }

    [Fact]
    public void AVeryLongDocumentKeepsAThumbYouCanSee()
    {
        var state = new ScrollState(0, 0, 0, 999_000, 400, 1_000_000);

        ScrollIndicator.Vertical(state, 400, 1000)!.Value.Height.Should().Be(ScrollIndicator.MinThumb);
    }

    [Fact]
    public void TheIndicatorStaysWhileScrollingThenFades()
    {
        ScrollIndicator.Opacity(0).Should().Be(1);
        ScrollIndicator.Opacity(ScrollIndicator.VisibleMs).Should().Be(1);
        ScrollIndicator.Opacity(ScrollIndicator.VisibleMs + ScrollIndicator.FadeMs / 2).Should().BeApproximately(0.5, 1e-9);
        ScrollIndicator.Opacity(ScrollIndicator.VisibleMs + ScrollIndicator.FadeMs).Should().Be(0);
    }

    [Fact]
    public void TheScrollIndicatorIsOnByDefault() => new PdfView().ShowScrollIndicator.Should().BeTrue();
}
