using ModelDisplayMode = Melville.Pdf.ViewerModel.DisplayMode;
using ModelViewState = Melville.Pdf.ViewerModel.PdfViewState;
using ModelZoomMode = Melville.Pdf.ViewerModel.ZoomMode;

namespace Plugin.Maui.PDFViewer;

/// <summary>How <see cref="PdfView"/> sizes the pages.</summary>
public enum ZoomMode
{
    /// <summary>The page width fills the view (the default).</summary>
    FitWidth,

    /// <summary>The whole page fits in the view.</summary>
    FitPage,

    /// <summary>The zoom the user chose by pinching, double-tapping or the zoom commands.</summary>
    Custom,
}

/// <summary>How <see cref="PdfView"/> shows the pages.</summary>
public enum DisplayMode
{
    /// <summary>All pages in one scrolling strip (the default).</summary>
    Continuous,

    /// <summary>One page at a time.</summary>
    SinglePage,
}

/// <summary>
/// Where a <see cref="PdfView"/> is in a document, for saving and restoring: the page (from 1), the zoom mode, the
/// scale (view units per PDF point) and the page point at the view's top-left corner.
/// </summary>
public readonly record struct PdfViewState(int Page, ZoomMode ZoomMode, double Zoom, double PageX = 0, double PageY = 0);

/// <summary>Arguments of <see cref="PdfView.PageChanged"/>.</summary>
/// <param name="CurrentPage">The page shown, from 1.</param>
/// <param name="TotalPages">The number of pages in the document.</param>
public sealed record PageChangedEventArgs(int CurrentPage, int TotalPages);

/// <summary>Conversions between the public types and the viewer model's.</summary>
internal static class ViewerModelMapping
{
    public static ModelZoomMode ToModel(this ZoomMode mode) => mode switch
    {
        ZoomMode.FitPage => ModelZoomMode.FitPage,
        ZoomMode.Custom => ModelZoomMode.Custom,
        _ => ModelZoomMode.FitWidth,
    };

    public static ZoomMode ToPublic(this ModelZoomMode mode) => mode switch
    {
        ModelZoomMode.FitPage => ZoomMode.FitPage,
        ModelZoomMode.Custom => ZoomMode.Custom,
        _ => ZoomMode.FitWidth,
    };

    public static ModelDisplayMode ToModel(this DisplayMode mode) =>
        mode == DisplayMode.SinglePage ? ModelDisplayMode.SinglePage : ModelDisplayMode.Continuous;

    public static ModelViewState ToModel(this PdfViewState state) =>
        new(state.Page, state.ZoomMode.ToModel(), state.Zoom, state.PageX, state.PageY);

    public static PdfViewState ToPublic(this ModelViewState state) =>
        new(state.Page, state.ZoomMode.ToPublic(), state.Zoom, state.PageX, state.PageY);
}
