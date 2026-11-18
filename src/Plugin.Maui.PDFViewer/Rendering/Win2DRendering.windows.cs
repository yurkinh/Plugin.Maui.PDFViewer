using System.Numerics;
using Melville.Pdf.MauiGraphics;
using Melville.Pdf.MauiGraphics.Drawing;
using Microsoft.Graphics.Canvas;
using Microsoft.Maui.Graphics.Platform;
using Windows.Graphics.DirectX;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Plugin.Maui.PDFViewer.Rendering;

/// <summary>
/// A Win2D canvas that behaves as the PDF renderer needs. Maui.Graphics' PlatformCanvas differs in two ways:
/// ConcatenateTransform post-concatenates (current × new), so a transform acts in device space instead of user space,
/// and a second clip in one saved state throws "Only one clip operation currently supported". ICanvas is implemented
/// again because the base members involved are not all virtual.
/// </summary>
internal sealed class Win2DPdfCanvas : PlatformCanvas, ICanvas
{
    // For each saved state: whether it has a clip, and the extra states pushed to hold further clips.
    private readonly Stack<(bool Clipped, int ExtraStates)> saved = new();
    private bool clipped;
    private int extraStates;

    /// <summary>Draw a page on any canvas of a Win2D drawing session, leaving the session as it was.</summary>
    public static void Draw(PdfDisplayList page, ICanvas canvas, IPdfImageFactory images, CanvasQuirks quirks, Matrix3x2 placement)
    {
        var platform = canvas as PlatformCanvas ?? (canvas as ScalingCanvas)?.ParentCanvas as PlatformCanvas;
        if (canvas is Win2DPdfCanvas || platform?.Session is not { } session)
        {
            page.Draw(canvas, images, quirks, placement);
            return;
        }
        var transform = session.Transform;
        var target = new Win2DPdfCanvas { Session = session, CanvasSize = platform.CanvasSize };
        try
        {
            target.ConcatenateTransform(transform);
            page.Draw(target, images, quirks, placement);
        }
        finally
        {
            target.CloseStates();
            session.Transform = transform;
        }
    }

    /// <summary>Back to the initial state, closing the clip layers still open: a session cannot end with any.</summary>
    public void CloseStates()
    {
        ResetState();
        saved.Clear();
        clipped = false;
        extraStates = 0;
    }

    void ICanvas.ConcatenateTransform(Matrix3x2 transform) => ConcatenateInUserSpace(transform);

    private void ConcatenateInUserSpace(Matrix3x2 transform)
    {
        // The base computes current × x; x = current⁻¹ × m × current makes that m × current. Going through it keeps
        // the saved states right, which setting the session's transform directly would not.
        var current = Session.Transform;
        base.ConcatenateTransform(Matrix3x2.Invert(current, out var inverse) ? inverse * transform * current : transform);
    }

    void ICanvas.SaveState()
    {
        SaveState();
        saved.Push((clipped, extraStates));
        clipped = false;
        extraStates = 0;
    }

    bool ICanvas.RestoreState()
    {
        for (; extraStates > 0; extraStates--) RestoreState();
        var restored = RestoreState();
        (clipped, extraStates) = saved.TryPop(out var state) ? state : default;
        return restored;
    }

    void ICanvas.ClipPath(PathF path, WindingMode windingMode) => Clip(path, windingMode);

    void ICanvas.ClipRectangle(float x, float y, float width, float height)
    {
        var rectangle = new PathF();
        rectangle.AppendRectangle(x, y, width, height);
        Clip(rectangle, WindingMode.NonZero);
    }

    private void Clip(PathF path, WindingMode windingMode)
    {
        if (clipped)
        {
            SaveState(); // clips nest, so a clip in a state of its own intersects with the ones before it
            extraStates++;
        }
        clipped = true;
        // The base intersects the clip with a rectangle of the canvas's size in the current coordinates, which cuts
        // it short under a scale. In device coordinates, with no transform, that rectangle is the canvas.
        var transform = Session.Transform;
        var device = new PathF(path);
        device.Transform(transform);
        Session.Transform = Matrix3x2.Identity;
        try
        {
            ClipPath(device, windingMode);
        }
        finally
        {
            Session.Transform = transform;
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
        canvas = new Win2DPdfCanvas { Session = session, CanvasSize = new global::Windows.Foundation.Size(width, height) };
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
        base.Dispose();
    }
}
