namespace Plugin.Maui.PDFViewer;

/// <summary>
/// A link in the document was tapped or clicked. Set <see cref="Handled"/> to stop <see cref="PdfView"/> from
/// following it: by default it shows the page a link goes to, and opens http, https and mailto addresses with the
/// system's browser or mail app. Other addresses are only reported here.
/// </summary>
public sealed class PdfLinkTappedEventArgs : EventArgs
{
    internal PdfLinkTappedEventArgs(int? pageNumber, string? uri) => (PageNumber, Uri) = (pageNumber, uri);

    /// <summary>The page the link goes to, for a link within the document; otherwise null.</summary>
    public int? PageNumber { get; }

    /// <summary>The address the link goes to, as written in the document, for a web or other link; otherwise null.</summary>
    public string? Uri { get; }

    /// <summary>Set to true when the app has dealt with the link, so the viewer does nothing more.</summary>
    public bool Handled { get; set; }
}
