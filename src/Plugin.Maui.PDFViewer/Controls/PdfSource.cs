using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Plugin.Maui.PDFViewer;

/// <summary>
/// Where a <see cref="PdfView"/> reads its document from. The document is opened when the viewer needs it,
/// and the stream is read to the end: PDF parsing needs random access.
/// </summary>
/// <remarks>
/// A string converts to a source, in XAML (<c>Source="manual.pdf"</c>) and in bindings to a string property: see
/// <see cref="FromString"/>.
/// </remarks>
[TypeConverter(typeof(PdfSourceConverter))]
public abstract class PdfSource
{
    /// <summary>Open the document's bytes. The caller disposes the stream.</summary>
    public abstract Task<Stream> OpenAsync(CancellationToken cancellation);

    /// <summary>A document produced by a stream factory, called each time the document is opened.</summary>
    public static PdfSource FromStream(Func<CancellationToken, Task<Stream>> open) => new StreamSource(open);

    /// <summary>A document in memory.</summary>
    public static PdfSource FromBytes(byte[] bytes) =>
        new StreamSource(_ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));

    /// <summary>A document in the file system.</summary>
    public static PdfSource FromFile(string path) =>
        new StreamSource(_ => Task.FromResult<Stream>(File.OpenRead(path)));

    /// <summary>
    /// A document chosen with FilePicker. Opened through <see cref="FileBase.OpenReadAsync"/>, never by path:
    /// on Android the result can be a content URI.
    /// </summary>
    public static PdfSource FromFileResult(FileResult file) => new StreamSource(_ => file.OpenReadAsync());

    /// <summary>A file bundled with the app (Resources/Raw).</summary>
    public static PdfSource FromAppPackage(string fileName) =>
        new StreamSource(_ => FileSystem.Current.OpenAppPackageFileAsync(fileName));

    /// <summary>
    /// A document at a URI: http and https are downloaded (into memory, no temporary file); file URIs are read from
    /// the file system.
    /// </summary>
    /// <param name="uri">An absolute http, https or file URI.</param>
    /// <param name="client">The HttpClient to download with; a shared one when null.</param>
    public static PdfSource FromUri(Uri uri, HttpClient? client = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri) throw new ArgumentException("The URI must be absolute.", nameof(uri));
        if (uri.IsFile) return FromFile(uri.LocalPath);
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException($"Unsupported URI scheme '{uri.Scheme}': use http, https or file.", nameof(uri));
        return new StreamSource(async cancellation =>
        {
            using var response = await (client ?? SharedHttpClient.Value)
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
            return new MemoryStream(bytes, writable: false);
        });
    }

    /// <summary>
    /// A document embedded in an assembly (an <c>EmbeddedResource</c> item). <paramref name="name"/> is the full
    /// manifest resource name or its end, such as <c>"manual.pdf"</c>; it must match exactly one resource.
    /// </summary>
    public static PdfSource FromEmbeddedResource(Assembly assembly, string name)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new StreamSource(_ =>
        {
            var names = assembly.GetManifestResourceNames();
            var match = names.Contains(name) ? name : names.Where(n => n.EndsWith(name, StringComparison.Ordinal)).ToArray() switch
            {
                [var single] => single,
                [] => throw new FileNotFoundException($"No embedded resource '{name}' in {assembly.GetName().Name}."),
                var several => throw new ArgumentException(
                    $"'{name}' matches several embedded resources: {string.Join(", ", several)}.", nameof(name)),
            };
            return Task.FromResult(assembly.GetManifestResourceStream(match)!);
        });
    }

    /// <summary>
    /// A source from a string: an http or https URL is downloaded (<see cref="FromUri"/>), a file URI or a rooted
    /// path is read from the file system (<see cref="FromFile"/>), and anything else is a file bundled with the app
    /// (<see cref="FromAppPackage"/>).
    /// </summary>
    public static PdfSource FromString(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.IsFile))
            return FromUri(uri);
        return Path.IsPathRooted(value) ? FromFile(value) : FromAppPackage(value);
    }

    /// <inheritdoc cref="FromString"/>
    public static implicit operator PdfSource?(string? value) => string.IsNullOrWhiteSpace(value) ? null : FromString(value);

    /// <inheritdoc cref="FromUri"/>
    public static implicit operator PdfSource?(Uri? uri) => uri is null ? null : FromUri(uri);

    private static readonly Lazy<HttpClient> SharedHttpClient = new(() => new HttpClient());

    private sealed class StreamSource(Func<CancellationToken, Task<Stream>> open) : PdfSource
    {
        public override Task<Stream> OpenAsync(CancellationToken cancellation) => open(cancellation);
    }
}

/// <summary>Converts strings to <see cref="PdfSource"/> in XAML, as <see cref="PdfSource.FromString"/> does.</summary>
public sealed class PdfSourceConverter : TypeConverter
{
    /// <inheritdoc/>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || sourceType == typeof(Uri) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc/>
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => value switch
    {
        string text => string.IsNullOrWhiteSpace(text) ? null : PdfSource.FromString(text),
        Uri uri => PdfSource.FromUri(uri),
        _ => base.ConvertFrom(context, culture, value),
    };
}
