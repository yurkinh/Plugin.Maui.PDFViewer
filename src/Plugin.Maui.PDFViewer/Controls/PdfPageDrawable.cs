using Plugin.Maui.PDFViewer.Rendering;
using Melville.Pdf.MauiGraphics.Drawing;
using Melville.Pdf.ViewerModel;
#if ANDROID || IOS || MACCATALYST || WINDOWS
using Microsoft.Maui.Graphics.Platform;
#endif
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Plugin.Maui.PDFViewer;

/// <summary>
/// Draws the pages in view into the viewer's GraphicsView: one page in single-page mode, the visible part of the
/// strip in continuous mode. Each page is drawn from bitmaps made off the UI thread (see <see cref="BitmapPlan"/>):
/// a whole-page backdrop, plus a detail bitmap of the visible region when zoomed in too far for a whole page at the
/// exact scale. During a gesture they are stretched until sharp ones are ready. The display list is not replayed on
/// the UI thread (on CoreGraphics a page of text takes ~140 ms): until a page's first bitmap is ready it is blank,
/// unless no bitmap could be made. Members are only touched on the UI thread.
/// </summary>
internal sealed class PdfPageDrawable : IDrawable
{
    public IReadOnlyList<PagePlacement<PdfDisplayList>> Placements { get; set; } = [];
    public Color Background { get; set; } = Colors.LightGray;
    public Color Shadow { get; set; } = Color.FromRgba(0, 0, 0, 0.12f);

    /// <summary>The search matches on a page, to highlight; null when nothing is searched.</summary>
    public Func<int, IReadOnlyList<SearchMatch>>? Matches { get; set; }

    /// <summary>The match being shown: highlighted more strongly than the others.</summary>
    public SearchMatch? CurrentMatch { get; set; }

    // Translucent, so the text under a highlight stays readable. (All-int arguments: with a float alpha, C# picks
    // the all-float overload and clamps 255 and 120 to 1.)
    public Color MatchHighlight { get; set; } = Color.FromRgba(255, 214, 0, 90);
    public Color CurrentMatchHighlight { get; set; } = Color.FromRgba(255, 120, 0, 128);

    /// <summary>The selected text, highlighted on its page.</summary>
    public TextSelection? Selection { get; set; }

    public Color SelectionHighlight { get; set; } = Color.FromRgba(0, 120, 215, 80);

    /// <summary>Bitmaps by page number. A bitmap whose display list is not the page's current one is ignored.</summary>
    public Dictionary<int, PageBitmaps> Bitmaps { get; } = [];

    /// <summary>Replay pages that have no bitmap (set after making one failed).</summary>
    public bool ReplayWithoutBitmap { get; set; } = !HasPlatformBitmaps;

    /// <summary>Back to the default: replay only where there are no platform bitmaps.</summary>
    public void ResetReplay() => ReplayWithoutBitmap = !HasPlatformBitmaps;

    // Plain .NET (unit tests, tools) has no platform bitmaps, so pages are replayed there.
#if ANDROID || IOS || MACCATALYST || WINDOWS
    private const bool HasPlatformBitmaps = true;
#else
    private const bool HasPlatformBitmaps = false;
#endif

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Background;
        canvas.FillRectangle(dirtyRect);
        foreach (var placement in Placements) DrawPage(canvas, placement);
    }

    private void DrawPage(ICanvas canvas, PagePlacement<PdfDisplayList> placement)
    {
        if (placement.Layout.Scale <= 0) return;
        DrawContent(canvas, placement);
        DrawMatches(canvas, placement);
        if (Selection is { } selected && selected.Page == placement.Page)
            DrawAreas(canvas, placement.Layout, selected.Areas, SelectionHighlight);
    }

    private static void DrawAreas(ICanvas canvas, PageLayout layout, IReadOnlyList<PageRect> areas, Color color)
    {
        var scale = (float)layout.Scale;
        canvas.FillColor = color;
        foreach (var area in areas)
            canvas.FillRectangle((float)(layout.OffsetX + area.X * scale), (float)(layout.OffsetY + area.Y * scale),
                (float)area.Width * scale, (float)area.Height * scale);
    }

    private void DrawMatches(ICanvas canvas, PagePlacement<PdfDisplayList> placement)
    {
        if (Matches?.Invoke(placement.Page) is not { Count: > 0 } matches) return;
        foreach (var match in matches)
            DrawAreas(canvas, placement.Layout, match.Areas,
                ReferenceEquals(match, CurrentMatch) ? CurrentMatchHighlight : MatchHighlight);
    }

    /// <summary>
    /// A soft shadow, like a card at 4dp elevation: layers that grow and fade outwards, offset downwards. Plain
    /// rectangles rather than a blurred shadow, which is slow on some platforms while scrolling.
    /// </summary>
    private void DrawShadow(ICanvas canvas, float x, float y, float width, float height)
    {
        if (Shadow.Alpha <= 0) return;
        const int layers = 4;
        for (var i = layers; i >= 1; i--)
        {
            canvas.FillColor = Shadow.WithAlpha(Shadow.Alpha / layers);
            canvas.FillRoundedRectangle(x - i * 0.5f, y - i * 0.25f + 1, width + i, height + i * 0.75f + 1, i);
        }
    }

    private void DrawContent(ICanvas canvas, PagePlacement<PdfDisplayList> placement)
    {
        var layout = placement.Layout;
        float x = (float)layout.OffsetX, y = (float)layout.OffsetY;
        float width = (float)layout.ContentWidth, height = (float)layout.ContentHeight;
        DrawShadow(canvas, x, y, width, height);
        // White until a bitmap is ready, and behind a detail bitmap that does not cover the view.
        canvas.FillColor = Colors.White;
        canvas.FillRectangle(x, y, width, height);
        if (placement.Rendered?.Content is not { } page) return;

        Bitmaps.TryGetValue(placement.Page, out var bitmaps);
        var backdrop = bitmaps?.Backdrop is { } b && ReferenceEquals(b.Page, page) ? b : null;
        var detail = bitmaps?.Detail is { } d && ReferenceEquals(d.Page, page) ? d : null;
        if (backdrop is null && detail is null)
        {
            if (ReplayWithoutBitmap) PdfPageDrawing.Draw(page, canvas, (float)layout.Scale, x, y);
            return;
        }
        // Exact size when settled, stretched mid-gesture.
        if (backdrop is not null) canvas.DrawImage(backdrop.Image, x, y, width, height);
        if (detail is not null)
        {
            var scale = (float)layout.Scale;
            var r = detail.Region;
            canvas.DrawImage(detail.Image, x + (float)r.X * scale, y + (float)r.Y * scale,
                (float)r.Width * scale, (float)r.Height * scale);
        }
    }
}

/// <summary>The cached bitmaps of one page.</summary>
internal sealed class PageBitmaps
{
    /// <summary>The whole page, possibly at a reduced scale.</summary>
    public PageBitmap? Backdrop { get; set; }

    /// <summary>Part of the page at the exact scale, when the backdrop is reduced.</summary>
    public PageBitmap? Detail { get; set; }

    public void Dispose()
    {
        Backdrop?.Dispose();
        Detail?.Dispose();
        Backdrop = Detail = null;
    }
}

/// <summary>A page drawn into a platform bitmap at one scale. Owns the export context, which owns the image.</summary>
internal sealed class PageBitmap : IDisposable
{
    /// <summary>Larger pages are not cached (memory); they are replayed directly.</summary>
    public const long MaxPixels = 6_000_000;

    private readonly IDisposable context;

    private PageBitmap(PdfDisplayList page, double scale, PageRect region, IImage image, IDisposable context)
    {
        Page = page;
        Scale = scale;
        Region = region;
        Image = image;
        this.context = context;
    }

    public PdfDisplayList Page { get; }
    public double Scale { get; }

    /// <summary>The part of the page drawn, in points.</summary>
    public PageRect Region { get; }

    public IImage Image { get; }

    /// <summary>
    /// Draw <paramref name="region"/> of the page (default: all of it) at <paramref name="scale"/> view units per
    /// point and <paramref name="density"/> pixels per unit. Null if that is more than <see cref="MaxPixels"/>.
    /// </summary>
    public static PageBitmap? Create(PdfDisplayList page, double scale, double density, PageRect? region = null)
    {
#if !(ANDROID || IOS || MACCATALYST || WINDOWS)
        return null; // plain .NET (unit tests, tools) has no platform bitmaps: the drawable replays the page
#else
        var area = region ?? new PageRect(0, 0, page.PageWidth, page.PageHeight);
        var pixelsPerPoint = scale * density;
        int width = (int)Math.Ceiling(area.Width * pixelsPerPoint);
        int height = (int)Math.Ceiling(area.Height * pixelsPerPoint);
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels) return null;
        var context = new PlatformBitmapExportService().CreateContext(width, height, 1);
        // White first: a region bitmap is drawn over the backdrop and must hide it.
        context.Canvas.FillColor = Colors.White;
        context.Canvas.FillRectangle(0, 0, width, height);
        PdfPageDrawing.Draw(page, context.Canvas, (float)pixelsPerPoint,
            (float)(-area.X * pixelsPerPoint), (float)(-area.Y * pixelsPerPoint));
        // On Android disposing the context can recycle its bitmap, so the context lives as long as the image.
        return new PageBitmap(page, scale, area, context.Image, context);
#endif
    }

    public void Dispose()
    {
        Image.Dispose();
        context.Dispose();
    }
}
