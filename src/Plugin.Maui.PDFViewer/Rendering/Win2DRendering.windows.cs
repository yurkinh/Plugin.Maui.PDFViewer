using System.Numerics;
using Melville.Pdf.MauiGraphics;
using Melville.Pdf.MauiGraphics.Drawing;
using Microsoft.Graphics.Canvas;
using Microsoft.Maui.Graphics.Text;
using Microsoft.Maui.Graphics.Platform;
using Windows.Graphics.DirectX;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Plugin.Maui.PDFViewer.Rendering;

/// <summary>
/// An adapter that applies the Win2D workarounds without losing the original canvas state. Maui.Graphics'
/// PlatformCanvas differs in two ways:
/// ConcatenateTransform post-concatenates (current × new), so a transform acts in device space instead of user space,
/// and a second clip in one saved state throws "Only one clip operation currently supported".
/// </summary>
internal sealed class Win2DPdfCanvas : ICanvas
{
    private readonly PlatformCanvas canvas;

    // For each saved state: whether it has a clip, and the extra states pushed to hold further clips.
    private readonly Stack<(bool Clipped, int ExtraStates)> saved = new();
    private bool clipped;
    private int extraStates;

    public Win2DPdfCanvas(PlatformCanvas canvas)
    {
        this.canvas = canvas;
        canvas.SaveState();
        saved.Push(default);
    }

    /// <summary>Draw a page on any canvas of a Win2D drawing session, leaving the session as it was.</summary>
    public static void Draw(PdfDisplayList page, ICanvas canvas, IPdfImageFactory images, CanvasQuirks quirks, Matrix3x2 placement)
    {
        var platform = canvas as PlatformCanvas ?? (canvas as ScalingCanvas)?.ParentCanvas as PlatformCanvas;
        if (canvas is Win2DPdfCanvas || platform?.Session is null)
        {
            page.Draw(canvas, images, quirks, placement);
            return;
        }
        var target = new Win2DPdfCanvas(platform);
        try
        {
            page.Draw(target, images, quirks, placement);
        }
        finally
        {
            target.CloseStates();
        }
    }

    /// <summary>Back to the initial state, closing the clip layers still open: a session cannot end with any.</summary>
    public void CloseStates()
    {
        while (saved.Count > 1) RestoreState();
        if (saved.Count > 0)
        {
            for (; extraStates > 0; extraStates--) canvas.RestoreState();
            canvas.RestoreState();
            saved.Pop();
        }
        saved.Clear();
        clipped = false;
        extraStates = 0;
    }

    public float DisplayScale { get => canvas.DisplayScale; set => canvas.DisplayScale = value; }
    public float StrokeSize { set => canvas.StrokeSize = value; }
    public float MiterLimit { set => canvas.MiterLimit = value; }
    public Color StrokeColor { set => canvas.StrokeColor = value; }
    public LineCap StrokeLineCap { set => canvas.StrokeLineCap = value; }
    public LineJoin StrokeLineJoin { set => canvas.StrokeLineJoin = value; }
    public float[] StrokeDashPattern { set => canvas.StrokeDashPattern = value; }
    public float StrokeDashOffset { set => canvas.StrokeDashOffset = value; }
    public Color FillColor { set => canvas.FillColor = value; }
    public Color FontColor { set => canvas.FontColor = value; }
    public IFont Font { set => canvas.Font = value; }
    public float FontSize { set => canvas.FontSize = value; }
    public float Alpha { set => canvas.Alpha = value; }
    public bool Antialias { set => canvas.Antialias = value; }
    public BlendMode BlendMode { set => canvas.BlendMode = value; }

    public void DrawPath(PathF path) => canvas.DrawPath(path);
    public void FillPath(PathF path, WindingMode windingMode) => canvas.FillPath(path, windingMode);
    public void SubtractFromClip(float x, float y, float width, float height) => canvas.SubtractFromClip(x, y, width, height);
    public void ClipPath(PathF path, WindingMode windingMode = WindingMode.NonZero) => Clip(path, windingMode);
    public void ClipRectangle(float x, float y, float width, float height)
    {
        var rectangle = new PathF();
        rectangle.AppendRectangle(x, y, width, height);
        Clip(rectangle, WindingMode.NonZero);
    }
    public void DrawLine(float x1, float y1, float x2, float y2) => canvas.DrawLine(x1, y1, x2, y2);
    public void DrawArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise, bool closed) =>
        canvas.DrawArc(x, y, width, height, startAngle, endAngle, clockwise, closed);
    public void FillArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise) =>
        canvas.FillArc(x, y, width, height, startAngle, endAngle, clockwise);
    public void DrawRectangle(float x, float y, float width, float height) => canvas.DrawRectangle(x, y, width, height);
    public void FillRectangle(float x, float y, float width, float height) => canvas.FillRectangle(x, y, width, height);
    public void DrawRoundedRectangle(float x, float y, float width, float height, float cornerRadius) =>
        canvas.DrawRoundedRectangle(x, y, width, height, cornerRadius);
    public void FillRoundedRectangle(float x, float y, float width, float height, float cornerRadius) =>
        canvas.FillRoundedRectangle(x, y, width, height, cornerRadius);
    public void DrawEllipse(float x, float y, float width, float height) => canvas.DrawEllipse(x, y, width, height);
    public void FillEllipse(float x, float y, float width, float height) => canvas.FillEllipse(x, y, width, height);
    public void DrawString(string value, float x, float y, HorizontalAlignment horizontalAlignment) =>
        canvas.DrawString(value, x, y, horizontalAlignment);
    public void DrawString(string value, float x, float y, float width, float height, HorizontalAlignment horizontalAlignment,
        VerticalAlignment verticalAlignment, TextFlow textFlow = TextFlow.ClipBounds, float lineSpacingAdjustment = 0) =>
        canvas.DrawString(value, x, y, width, height, horizontalAlignment, verticalAlignment, textFlow, lineSpacingAdjustment);
    public void DrawText(IAttributedText value, float x, float y, float width, float height) =>
        canvas.DrawText(value, x, y, width, height);
    public void Rotate(float degrees, float x, float y) => canvas.Rotate(degrees, x, y);
    public void Rotate(float degrees) => canvas.Rotate(degrees);
    public void Scale(float sx, float sy) => canvas.Scale(sx, sy);
    public void Translate(float tx, float ty) => canvas.Translate(tx, ty);
    public void ConcatenateTransform(Matrix3x2 transform) => ConcatenateInUserSpace(transform);
    public void SetShadow(SizeF offset, float blur, Color color) => canvas.SetShadow(offset, blur, color);
    public void SetFillPaint(Paint paint, RectF rectangle) => canvas.SetFillPaint(paint, rectangle);
    public void DrawImage(IImage image, float x, float y, float width, float height) => canvas.DrawImage(image, x, y, width, height);
    public SizeF GetStringSize(string value, IFont font, float fontSize) => canvas.GetStringSize(value, font, fontSize);
    public SizeF GetStringSize(string value, IFont font, float fontSize, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment) =>
        canvas.GetStringSize(value, font, fontSize, horizontalAlignment, verticalAlignment);

    private void ConcatenateInUserSpace(Matrix3x2 transform)
    {
        // PlatformCanvas computes current × x; x = current⁻¹ × m × current makes that m × current. Going through it
        // keeps the saved states right, which setting the session's transform directly would not.
        var current = canvas.Session.Transform;
        canvas.ConcatenateTransform(Matrix3x2.Invert(current, out var inverse) ? inverse * transform * current : transform);
    }

    public void SaveState()
    {
        canvas.SaveState();
        saved.Push((clipped, extraStates));
        clipped = false;
        extraStates = 0;
    }

    public bool RestoreState()
    {
        for (; extraStates > 0; extraStates--) canvas.RestoreState();
        if (saved.Count <= 1) return false;
        var restored = canvas.RestoreState();
        (clipped, extraStates) = saved.TryPop(out var state) ? state : default;
        return restored;
    }

    public void ResetState()
    {
        CloseStates();
        canvas.SaveState();
        saved.Push(default);
    }

    private void Clip(PathF path, WindingMode windingMode)
    {
        if (clipped)
        {
            canvas.SaveState(); // clips nest, so a clip in a state of its own intersects with the ones before it
            extraStates++;
        }
        clipped = true;
        // PlatformCanvas intersects the clip with a rectangle of the canvas's size in the current coordinates, which cuts
        // it short under a scale. In device coordinates, with no transform, that rectangle is the canvas.
        var transform = canvas.Session.Transform;
        var device = new PathF(path);
        device.Transform(transform);
        canvas.Session.Transform = Matrix3x2.Identity;
        try
        {
            canvas.ClipPath(device, windingMode);
        }
        finally
        {
            canvas.Session.Transform = transform;
        }
    }
}

/// <summary>
/// Creates Win2D bitmaps from premultiplied BGRA pixels, on any thread. They live on the shared device, which is the
/// one GraphicsView draws with.
/// </summary>
public sealed class Win2DImageFactory : IPdfImageFactory
{
    /// <summary>The shared instance.</summary>
    public static Win2DImageFactory Instance { get; } = new();

    /// <inheritdoc />
    public IImage Create(PdfImage image)
    {
        var device = CanvasDevice.GetSharedDevice();
        var bitmap = CanvasBitmap.CreateFromBytes(
            device, image.Pbgra, image.Width, image.Height, DirectXPixelFormat.B8G8R8A8UIntNormalized);
        return new PlatformImage(device, bitmap);
    }
}

/// <summary>
/// An offscreen Win2D canvas. Maui.Graphics' PlatformBitmapExportService is not implemented on Windows. Reading
/// <see cref="Image"/> ends the drawing; the image then owns the bitmap.
/// </summary>
internal sealed class Win2DBitmapContext : BitmapExportContext
{
    private readonly CanvasRenderTarget target;
    private readonly Win2DPdfCanvas canvas;
    private CanvasDrawingSession? session;
    private IImage? image;

    public Win2DBitmapContext(int width, int height) : base(width, height, 96)
    {
        // Pixels, whatever the display's scale: one canvas unit is one pixel, as on the other platforms.
        target = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), width, height, 96);
        session = target.CreateDrawingSession();
        session.Clear(global::Windows.UI.Color.FromArgb(0, 0, 0, 0)); // a new render target's pixels are undefined
        canvas = new Win2DPdfCanvas(new PlatformCanvas
        {
            Session = session,
            CanvasSize = new global::Windows.Foundation.Size(width, height)
        });
    }

    public override ICanvas Canvas => canvas;

    public override IImage Image
    {
        get
        {
            EndDrawing();
            return image ??= new PlatformImage(target.Device, target);
        }
    }

    public override void WriteToStream(Stream stream) => Image.Save(stream);

    private void EndDrawing()
    {
        if (session is null) return;
        canvas.CloseStates();
        session.Dispose();
        session = null;
    }

    public override void Dispose()
    {
        EndDrawing();
        if (image is null) target.Dispose();
        else image.Dispose();
        base.Dispose();
    }
}
