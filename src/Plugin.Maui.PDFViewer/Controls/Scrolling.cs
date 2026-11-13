using System.Diagnostics;

namespace Plugin.Maui.PDFViewer;

/// <summary>A millisecond clock for gestures and animations.</summary>
internal static class GestureClock
{
    public static double NowMs => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
}

/// <summary>
/// The finger's velocity at the end of a drag, from the drag's last <see cref="WindowMs"/> milliseconds. A finger
/// that rested before lifting (no movement for <see cref="RestMs"/>) has no velocity, so it does not fling.
/// </summary>
internal sealed class FlingTracker
{
    public const double WindowMs = 100;
    public const double RestMs = 50;

    private readonly List<(double T, double X, double Y)> samples = [];

    public void Reset() => samples.Clear();

    /// <summary>The drag's total translation at <paramref name="timeMs"/>.</summary>
    public void Add(double timeMs, double x, double y)
    {
        samples.Add((timeMs, x, y));
        while (samples.Count > 2 && timeMs - samples[0].T > WindowMs) samples.RemoveAt(0);
    }

    /// <summary>View units per millisecond, in the direction the finger moved; zero when it rested.</summary>
    public (double X, double Y) Velocity(double nowMs)
    {
        if (samples.Count < 2) return (0, 0);
        var (t1, x1, y1) = samples[^1];
        if (nowMs - t1 > RestMs) return (0, 0);
        var (t0, x0, y0) = samples[0];
        var dt = t1 - t0;
        return dt <= 0 ? (0, 0) : ((x1 - x0) / dt, (y1 - y0) / dt);
    }
}

/// <summary>
/// Momentum after a drag: the content keeps moving and slows down exponentially, as native scroll views do.
/// </summary>
internal static class Fling
{
    /// <summary>How quickly a fling slows down: the speed falls to 37% every 325 ms (about iOS's normal rate).</summary>
    public const double TimeConstantMs = 325;

    /// <summary>Slower drags do not fling (view units per ms).</summary>
    public const double MinStartSpeed = 0.25;

    /// <summary>A fling stops below this speed (view units per ms).</summary>
    public const double MinSpeed = 0.02;

    /// <summary>The fastest fling (view units per ms), so a twitch does not throw the document to its end.</summary>
    public const double MaxSpeed = 8;

    public static bool ShouldStart((double X, double Y) velocity) =>
        Math.Max(Math.Abs(velocity.X), Math.Abs(velocity.Y)) >= MinStartSpeed;

    public static (double X, double Y) Clamp((double X, double Y) velocity) =>
        (Math.Clamp(velocity.X, -MaxSpeed, MaxSpeed), Math.Clamp(velocity.Y, -MaxSpeed, MaxSpeed));

    /// <summary>How far the content moves in <paramref name="dtMs"/> starting at <paramref name="velocity"/>.</summary>
    public static (double X, double Y) Distance((double X, double Y) velocity, double dtMs)
    {
        var travelled = TimeConstantMs * (1 - Math.Exp(-dtMs / TimeConstantMs));
        return (velocity.X * travelled, velocity.Y * travelled);
    }

    /// <summary>The velocity <paramref name="dtMs"/> later.</summary>
    public static (double X, double Y) Decay((double X, double Y) velocity, double dtMs)
    {
        var factor = Math.Exp(-dtMs / TimeConstantMs);
        return (velocity.X * factor, velocity.Y * factor);
    }

    public static bool IsMoving((double X, double Y) velocity) =>
        Math.Abs(velocity.X) > MinSpeed || Math.Abs(velocity.Y) > MinSpeed;
}

/// <summary>How far the content is scrolled, for the scroll indicators.</summary>
internal readonly record struct ScrollState(
    double ScrollX, double ScrollY, double MaxScrollX, double MaxScrollY, double ContentWidth, double ContentHeight)
{
    public bool CanScroll => MaxScrollX > 0.5 || MaxScrollY > 0.5;
}

/// <summary>
/// Where the scroll indicator thumbs go: along the right edge for vertical scrolling and the bottom edge for
/// horizontal, sized by how much of the content is in view (at least <see cref="MinThumb"/>).
/// </summary>
internal static class ScrollIndicator
{
    public const float Thickness = 4;
    public const float Inset = 3;
    public const float MinThumb = 32;

    /// <summary>Shown fully for this long after the last scroll, then faded out over <see cref="FadeMs"/>.</summary>
    public const double VisibleMs = 800;
    public const double FadeMs = 300;

    public static double Opacity(double msSinceScroll) =>
        msSinceScroll <= VisibleMs ? 1 : Math.Max(0, 1 - (msSinceScroll - VisibleMs) / FadeMs);

    /// <summary>The vertical thumb, or null when the content does not scroll vertically.</summary>
    public static RectF? Vertical(ScrollState state, float viewWidth, float viewHeight)
    {
        if (state.MaxScrollY <= 0.5 || state.ContentHeight <= 0) return null;
        var track = viewHeight - 2 * Inset - (state.MaxScrollX > 0.5 ? Thickness + Inset : 0);
        if (track <= MinThumb) return null;
        var thumb = Math.Clamp(track * viewHeight / (float)state.ContentHeight, MinThumb, track);
        var y = Inset + (track - thumb) * (float)Math.Clamp(state.ScrollY / state.MaxScrollY, 0, 1);
        return new RectF(viewWidth - Inset - Thickness, y, Thickness, thumb);
    }

    /// <summary>The horizontal thumb, or null when the content does not scroll horizontally.</summary>
    public static RectF? Horizontal(ScrollState state, float viewWidth, float viewHeight)
    {
        if (state.MaxScrollX <= 0.5 || state.ContentWidth <= 0) return null;
        var track = viewWidth - 2 * Inset - (state.MaxScrollY > 0.5 ? Thickness + Inset : 0);
        if (track <= MinThumb) return null;
        var thumb = Math.Clamp(track * viewWidth / (float)state.ContentWidth, MinThumb, track);
        var x = Inset + (track - thumb) * (float)Math.Clamp(state.ScrollX / state.MaxScrollX, 0, 1);
        return new RectF(x, viewHeight - Inset - Thickness, thumb, Thickness);
    }
}
