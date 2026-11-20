# Plugin.Maui.PDFViewer

`PdfView` is a PDF viewer control for **.NET MAUI 11** on Android, iOS, Mac Catalyst and Windows.

Pages are rendered by [Melville.Pdf](https://github.com/DrJohnMelville/Pdf), a PDF renderer written entirely in .NET, and drawn with Microsoft.Maui.Graphics. There are no native PDF libraries, so a page looks the same on every platform. The package is trim and Native AOT compatible.

## Features

- **Continuous scroll** through all pages (the default), vertically or horizontally, or one page at a time. Pages fit the width and sit on a white background with a soft shadow.
- **Smooth gestures:** pinch to zoom, drag with momentum, double-tap to zoom in and back. A thin scroll indicator shows where you are while scrolling.
- **Search** with highlighted matches, **text selection and copy**, and **links** (within the document, web and mail).
- **Optional toolbar** (off by default): page navigation, zoom, find, layers. Or build your own with the public commands.
- **Page appearance:** shadow, margins and crop.
- **Encrypted PDFs**, **layers** (optional content), and a two-way **`ViewState`** to save and restore the reading position.
- **Sources:** files, URLs, app package files, embedded resources, `FilePicker` results, byte arrays and streams; a plain string works in XAML and bindings.
- **Accessibility:** screen readers read the page's text; named controls; 44 × 44 touch targets; desktop keyboard and mouse-wheel support.

## Getting started

```
dotnet add package Plugin.Maui.PDFViewer --prerelease
```

Register it in `MauiProgram.cs`:

```csharp
builder
    .UseMauiApp<App>()
    .UseMauiPdf();
```

Show a PDF:

```xml
<ContentPage xmlns:pdf="https://github.com/yurkinh/Plugin.Maui.PDFViewer" ...>
    <pdf:PdfView Source="{Binding PdfPath}"
                 PageNumber="{Binding PageNumber}"
                 PageChangedCommand="{Binding PageChangedCommand}" />
</ContentPage>
```

`Source` takes a `PdfSource`, or a string: an `http`/`https` URL is downloaded, a `file:` URI or rooted path is read from disk, and anything else is a file bundled with the app (`Resources/Raw`).

```csharp
PdfSource.FromAppPackage("manual.pdf");
PdfSource.FromFile(path);
PdfSource.FromUri(new Uri("https://example.com/manual.pdf"));
PdfSource.FromEmbeddedResource(typeof(App).Assembly, "manual.pdf");
PdfSource.FromFileResult(await FilePicker.Default.PickAsync());
PdfSource.FromBytes(bytes);
PdfSource.FromStream(ct => OpenMyStreamAsync(ct));
```

## Main properties

| Property | Default | |
|---|---|---|
| `Source` | | The document. |
| `PageNumber` / `PageCount` | | The page shown (from 1, two-way) and the number of pages. |
| `DisplayMode` | `Continuous` | Or `SinglePage`. |
| `ScrollOrientation` | `Vertical` | Or `Horizontal`. |
| `ZoomMode` | `FitWidth` | Or `FitPage`; `Custom` after zooming. |
| `MinZoom` / `MaxZoom` | 1 / 4 | Multiples of fit width. |
| `ShowToolbar` | false | The built-in toolbar. |
| `ShowScrollIndicator` | true | The scroll indicator. |
| `PageAppearance` | | `ShadowEnabled`, `Margin`, `Crop`. |
| `ViewState` | | Page, zoom and scroll, for saving and restoring. |
| `PasswordSource` | | Supplies passwords for encrypted documents. |

Events: `PageChanged` (also `PageChangedCommand`), `DocumentLoaded`, `LoadFailed`, `LinkTapped`. Methods: `SearchAsync`, `ClearSearch`, `ClearSelection`, `GetPageTextAsync`. Commands for your own UI: first, previous, next and last page, zoom in and out, fit page, fit width, next and previous search result, copy, select page.

The full API, with examples, is in the [README on GitHub](https://github.com/yurkinh/Plugin.Maui.PDFViewer#readme).

## Coming from Maui.PDFView

| Maui.PDFView | Plugin.Maui.PDFViewer |
|---|---|
| `.UseMauiPdfView()` | `.UseMauiPdf()` |
| `xmlns:pdf="clr-namespace:Maui.PDFView;assembly=Maui.PDFView"` | `xmlns:pdf="https://github.com/yurkinh/Plugin.Maui.PDFViewer"` |
| `Uri="{Binding PdfPath}"` | `Source="{Binding PdfPath}"` |
| `IsHorizontal="True"` | `ScrollOrientation="Horizontal"` |
| `MaxZoom`, `PageAppearance` | `MaxZoom` (and `MinZoom`), `PageAppearance` |
| `PageIndex` (from 0) | `PageNumber` (from 1) |
| `PageChangedCommand` | `PageChangedCommand` or `PageChanged` |

## Requirements

- .NET 11 and .NET MAUI 11.0.0 or later.
- Android API 24, iOS 15, Mac Catalyst 17, Windows 10.0.17763.
- iOS and Mac Catalyst apps build with Xcode 27.2 (MAUI 11.0.0). Apps built with the iOS 27 SDK must use the UIKit scene lifecycle (a `UIApplicationSceneManifest` in `Info.plist` and a scene delegate); see https://aka.ms/uiscene.
- Fonts are included for documents that don't embed theirs: Latin, Greek, Cyrillic and symbols. Chinese, Japanese and Korean documents need embedded fonts, or your own font mapper (`UseMauiPdf(o => o.FontMapper = ...)`).
- The Melville.Pdf assemblies the viewer uses are inside this package. Don't also reference the upstream `Melville.Pdf.*` packages: the app would get two copies of the same assemblies.

## Limitations

No annotation editing, form filling or printing. A text selection stays on one page. On Windows, very sharp line corners overshoot slightly (Win2D). See [Limitations](https://github.com/yurkinh/Plugin.Maui.PDFViewer#limitations) for details.

## License

MIT. Rendering by [Melville.Pdf](https://github.com/DrJohnMelville/Pdf) (MIT) by John Melville. Bundled code and fonts are listed in THIRD-PARTY-NOTICES.md inside the package and [on GitHub](https://github.com/yurkinh/Plugin.Maui.PDFViewer/blob/main/THIRD-PARTY-NOTICES.md).

Issues and feedback: https://github.com/yurkinh/Plugin.Maui.PDFViewer/issues
