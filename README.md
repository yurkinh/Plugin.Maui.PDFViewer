# Plugin.Maui.PDFViewer

`PdfView` is a PDF viewer control for .NET MAUI 11 (.NET 11). It runs on Android, iOS, Mac Catalyst and Windows.

Pages are rendered by [Melville.Pdf](https://github.com/DrJohnMelville/Pdf), a PDF renderer written entirely in .NET, and drawn with Microsoft.Maui.Graphics. There are no native PDF libraries, so a page looks the same on every platform. The package is trim and Native AOT compatible.

> **Preview.** Version 0.1.0-preview.1. It is not on nuget.org yet; see [Building the package](#building-the-package).

## Features

- **Display modes:** **continuous scroll** through all pages (the default), vertically or horizontally, or one page at a time. Pages fit the width by default and sit on a white background with a soft shadow. Only the pages near the view are rendered.
- **Gestures:** pinch to zoom around your fingers, drag to pan, double-tap to switch between fit width and 2×.
- **Toolbar:** first, previous, next and last page; a page number box; zoom in and out; fit page; fit width; single page or continuous scroll; find; layers. It is off by default: turn it on with `ShowToolbar="True"`, or use your own controls with the public commands.
- **Links:** tap a link to go to the page it points to, at the position it names. Web and mail links open in the browser or mail app. The `LinkTapped` event lets the app handle links itself.
- **Search:** the Find button opens a find bar. Matches are highlighted on every page, the current one in orange, and the previous and next buttons step through them. Search starts at the current page and shows its progress.
- **Selecting and copying text:** press and hold on a word to select it, then drag without lifting your finger to select more. A bar offers Copy and Select page, and a tap clears the selection.
- **Layers:** optional content groups can be shown and hidden.
- **Encrypted PDFs:** plug in your own `IPasswordSource` to supply passwords.
- **Sources:** app package files, the file system, `FilePicker` results (including Android content URIs), byte arrays and streams.
- **Saving the reading position:** the two-way `ViewState` holds the page, zoom and scroll, so you can save it when the app is backgrounded and restore it later.
- **Accessibility:** the page reads out its text when a screen reader focuses it, screen reader announcements, named controls, 44 × 44 touch targets and desktop keyboard commands.
- **Dark theme:** the area around the page follows the app theme; the page itself stays white.
- **Fonts:** fonts are included for documents that don't embed theirs, since iOS and Android have no system font folder to fall back on.

## Getting started

### 1. Install

```
dotnet add package Plugin.Maui.PDFViewer --prerelease
```

### 2. Register

```csharp
// MauiProgram.cs
var builder = MauiApp.CreateBuilder();
builder
    .UseMauiApp<App>()
    .UseMauiPdf();
```

`UseMauiPdf(o => o.FontMapper = ...)` changes the fonts used for documents that do not embed theirs. The included fonts cover Latin, Greek, Cyrillic and symbols. Chinese, Japanese and Korean documents need fonts embedded in the document, or your own `IDefaultFontMapper`.

### 3. Show a PDF

```xml
<ContentPage xmlns:pdf="https://github.com/yurkinh/Plugin.Maui.PDFViewer" ...>
    <pdf:PdfView Source="{Binding Source}"
                 PageNumber="{Binding PageNumber}" />
</ContentPage>
```

```csharp
Source = PdfSource.FromAppPackage("manual.pdf");          // Resources/Raw/manual.pdf
Source = PdfSource.FromFileResult(await FilePicker.Default.PickAsync(...));
Source = PdfSource.FromFile(path);
Source = PdfSource.FromUri(new Uri("https://example.com/manual.pdf")); // downloaded into memory
Source = PdfSource.FromEmbeddedResource(typeof(App).Assembly, "manual.pdf");
Source = PdfSource.FromBytes(bytes);
Source = PdfSource.FromStream(async ct => await http.GetStreamAsync(url, ct));
```

A string works too, in XAML (`Source="manual.pdf"`) and when `Source` is bound to a string property: an `http` or `https` URL is downloaded, a `file:` URI or a rooted path is read from the file system, and anything else is a file bundled with the app.

Apps that use .NET MAUI 10's implicit (global) XAML namespaces can write `<PdfView />` with no prefix.

## API

### Properties

| Property | Type | | Description |
|---|---|---|---|
| `Source` | `PdfSource?` | | The document. Setting it closes the previous one. |
| `PasswordSource` | `IPasswordSource?` | | Supplies passwords for encrypted documents. Set it before `Source`. |
| `PageNumber` | `int` | two-way | The page shown, starting at 1. |
| `PageCount` | `int` | read-only | The number of pages, or 0 when no document is open. |
| `ZoomMode` | `ZoomMode` | two-way | `FitWidth` (the default), `FitPage`, or `Custom` after zooming. |
| `MaxZoom` | `double` | | The largest zoom as a multiple of fit width. The default is 4; values below 1 are ignored. |
| `MinZoom` | `double` | | The smallest zoom as a multiple of fit width. The default is 1, so zooming out stops at fit width; fit page stays reachable when it is smaller. |
| `DisplayMode` | `DisplayMode` | two-way | `Continuous` (the default) or `SinglePage`. The toolbar button switches between them. |
| `ScrollOrientation` | `ScrollOrientation` | | In continuous mode: `Vertical` (the default) or `Horizontal`. This is MAUI's `ScrollView` enum; `Both` and `Neither` mean vertical. |
| `ViewState` | `PdfViewState?` | two-way | The page, zoom mode, zoom and scroll. Set it before `Source` to open at a saved position. It works in both display modes. |
| `PageChangedCommand` | `ICommand?` | | Executed with `PageChangedEventArgs` whenever `PageChanged` is raised. |
| `IsLoading` | `bool` | read-only | True while a document is opening. |
| `ErrorMessage` | `string?` | read-only | Why the document or the page could not be shown. |
| `ShowToolbar` | `bool` | | Shows the built-in toolbar. The default is false. |
| `BackgroundColor` | `Color?` | | Behind the pages. When not set: white, or dark grey in the dark theme. |
| `IsSearchVisible` | `bool` | two-way | Shows the find bar. Hiding it clears the search. |
| `SearchResultCount` | `int` | read-only | The matches found so far. |
| `CurrentSearchResult` | `int` | read-only | The match shown, counting from 1; 0 when there is none. |
| `IsSearching` | `bool` | read-only | True while a search is still reading pages. |
| `SelectedText` | `string?` | read-only | The selected text, with spaces between words and line breaks between lines, or null. |

### Commands

`FirstPageCommand`, `PreviousPageCommand`, `NextPageCommand`, `LastPageCommand`, `ZoomInCommand`, `ZoomOutCommand`, `FitPageCommand`, `FitWidthCommand`, `NextSearchResultCommand`, `PreviousSearchResultCommand`, `CopySelectionCommand` and `SelectAllCommand` (the current page) are for your own toolbars, menus and keyboard shortcuts. Their `CanExecute` follows the open document, and `CanExecuteChanged` is raised on the UI thread.

### Methods

`SearchAsync(text, matchCase = false)` finds text in every page, highlights the matches and shows the first one on or after the current page. It returns the number of matches once every page has been read. Case is ignored unless `matchCase` is true. Any white space in `text` matches a space or a line break, so a phrase is found even when it wraps onto the next line. `ClearSearch()` removes the highlights.

```csharp
var count = await pdfView.SearchAsync("optional content");
if (count > 0) pdfView.NextSearchResultCommand.Execute(null);
```

`ClearSelection()` selects nothing.

`GetPageTextAsync(pageNumber)` returns the text of a page, with spaces between words and line breaks between lines, for copying, sharing or reading aloud. A scanned page without recognised text has none.

### Events

`PageChanged` is raised with `PageChangedEventArgs(CurrentPage, TotalPages)` when the page shown or the page count changes while a document is open: once it opens, and after scrolling, navigating or searching. `PageChangedCommand` is executed with the same arguments.

`DocumentLoaded` is raised once the first page is shown. `LoadFailed` is raised with the exception when a document cannot be opened. Both are raised on the UI thread.

`LinkTapped` is raised on the UI thread when a link is tapped or clicked, before the viewer follows it. Its arguments carry either `PageNumber` (a link within the document) or `Uri` (any other link). Set `Handled` to stop the viewer from following the link:

```csharp
pdfView.LinkTapped += (_, e) =>
{
    // Open only secure web links; the viewer still follows links within the document.
    if (e.Uri is { } address && !address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        e.Handled = true;
};
```

Without a handler, links within the document go to their page and keep the zoom. Links to `http`, `https` and `mailto` addresses open with `Launcher`. Links to other addresses, such as `file:` or app schemes, do nothing; the app can still handle them in `LinkTapped`.

The viewer reads the link destinations PDF defines: explicit destinations, named destinations, and GoTo, URI and Named (next, previous, first and last page) actions. Links to other files (GoToR), launch actions, JavaScript and form actions are ignored.

### Saving the reading position

```csharp
// Save when the window stops (and when it is destroyed: Android's back button can skip Stopped).
Preferences.Set("position", JsonSerializer.Serialize(viewModel.ViewState, MyJsonContext.Default.PdfViewState));

// Restore: set ViewState, then Source.
viewModel.ViewState = saved;
viewModel.Source = PdfSource.FromAppPackage("manual.pdf");
```

### Passwords

```csharp
public sealed class AskForPassword : IPasswordSource
{
    // Called on a worker thread, again after each wrong password. Return null to cancel opening.
    public async ValueTask<(string?, PasswordType)> GetPasswordAsync() =>
        (await MainThread.InvokeOnMainThreadAsync(ShowMaskedPromptAsync), PasswordType.User);
}
```

`DisplayPromptAsync` cannot mask what is typed. The sample app has a small modal page that does.

## Platform notes

| Platform | Minimum | Notes |
|---|---|---|
| Android | API 24 | No permissions needed. `FilePicker` returns content URIs; `PdfSource.FromFileResult` reads them. |
| iOS | 15.0 | Apps need Xcode 27.2 with MAUI 11.0.0 (the iOS 27.2 SDK); older Xcode fails with MT180. |
| Mac Catalyst | 17.0 | Xcode 27.2, as for iOS. To open picked files in a sandboxed app, add `com.apple.security.files.user-selected.read-only`. Trackpad scrolling pans the page. |
| Windows | 10.0.17763 | The mouse wheel scrolls; Shift + wheel scrolls sideways and Ctrl + wheel zooms around the pointer. Built in CI, but not yet tested on a device. |

## Performance

A page is drawn once into a display list on a worker thread. The viewer then draws cached bitmaps: one for the whole page, plus one for the visible area when you zoom in a long way. It never replays the page on the UI thread, which matters most on iOS and Mac: CoreGraphics takes about 140 ms to fill a page of text, while drawing the cached bitmap takes 0.2 ms. A new page shows white until its bitmap is ready.

Release builds on a Motorola Edge 60 Neo (Android 16). The package targets .NET 11, which runs on CoreCLR on Android, iOS and Mac Catalyst and with runtime async; .NET 10 (Mono) is shown for comparison:

| Motorola Edge 60 Neo | .NET 10 | .NET 11 |
|---|---|---|
| Record a page of text | 260 ms | 60 ms |
| Replay it into a bitmap (800 × 1035 px) | 74 ms | 44 ms |
| Draw the cached bitmap (a frame) | 0.9 ms | 0.9 ms |
| Search all 120 pages of the long sample | 32 s | 5 s |
| Render every sample and reference document (251 documents, 390 pages) | 49 s | 13 s |

On an iPhone 13 (.NET 11, Release): recording that page takes 62 ms, replaying it into a bitmap 125 ms (CoreGraphics is slow at filling text, which is why the viewer caches bitmaps), and drawing the cached bitmap 0.26 ms; all 251 documents render in 11 s. A Native AOT build on a Mac renders them in 2 s.

Search reads each page's text with a pass like rendering that draws nothing. The first match appears as soon as its page is read. The plain text of every page read is kept, so later searches of the same document read only the pages that match.

## Limitations

- No annotation editing, form filling or printing.
- A selection stays on one page, and has no handles to adjust it after you lift your finger. On desktop you also press and hold, because a mouse drag scrolls.
- Search does not join words hyphenated at the end of a line, and matches text in the order it is drawn, which for some documents is not reading order.
- No "back" after following a link, and no pointer cursor over links on desktop.
- Links can't be reached with a screen reader yet.
- A screen reader reads the page's text in one go; it can't move through it line by line.
- Windows has not been tested on a device.

## Repository layout

This repository follows the [Plugin.Maui.Feature](https://github.com/jfversluis/Plugin.Maui.Feature) template.

| Path | Contents |
|---|---|
| `src/Plugin.Maui.PDFViewer` | The plugin. Almost everything is shared, because pages are drawn with Maui.Graphics. The few platform files use the template's suffixes (`*.android.cs`, `*.macios.cs`). It also targets plain `net10.0`, for unit tests. |
| `samples/Plugin.Maui.PDFViewer.Sample` | The sample to copy from: one page with a `PdfView`. |
| `samples/Plugin.Maui.PDFViewer.TestApp` | A diagnostics app: a library of bundled PDFs, a gallery of about 240 reference documents, timings, a smoke test, and an unattended run (`MELVILLE_AUTORUN=1`). |
| `tests/Plugin.Maui.PDFViewer.Tests` | Tests of the control, and of the bundled sample PDFs. |
| `external/Pdf` | The Melville.Pdf renderer, a fork of [DrJohnMelville/Pdf](https://github.com/DrJohnMelville/Pdf), as a git submodule. |
| `tools`, `pack-nuget.sh` | The sample corpus generator, packaging, and a check that the package works in a new app. |

## Building

```bash
git clone --recursive https://github.com/yurkinh/Plugin.Maui.PDFViewer.git
cd Plugin.Maui.PDFViewer
./external/Pdf/Src/install-dotnet.sh         # the .NET 11 SDK global.json asks for (the fork's libraries also need the .NET 10 runtime)
external/Pdf/Src/.dotnet/dotnet workload install maui
./external/Pdf/build-local-packages.sh       # Melville.StreamInterfaces, from the fork's submodule
external/Pdf/Src/.dotnet/dotnet build samples/Plugin.Maui.PDFViewer.Sample.slnx
external/Pdf/Src/.dotnet/dotnet test --project tests/Plugin.Maui.PDFViewer.Tests/Plugin.Maui.PDFViewer.Tests.csproj
```

## Building the package

```bash
./pack-nuget.sh                                         # writes artifacts/nuget/Plugin.Maui.PDFViewer.*.nupkg
./tools/test-package-consumer.sh net11.0-android net11.0-ios
```

The package targets .NET 11 only and is self-contained. The Melville.Pdf assemblies it uses are built from the fork in `external/Pdf` and bundled in its `lib` folders: the fork adds the MAUI renderer, viewer model, links and text layer to upstream, and the `Melville.*` IDs on nuget.org belong to the upstream author. Its dependencies are packages on nuget.org: Microsoft.Maui.Controls, CoreJ2K, Melville.Hacks, Melville.INPC and Melville.Linq. `pack-nuget.sh` fails if any target framework lacks a bundled assembly, if a dependency is something else, or if those versions drift from the fork's. CI packs on every push and uploads the package as a build artifact.

Because the Melville assemblies are bundled, an app should not also reference upstream's Melville.Pdf packages: it would get two copies of the same assemblies.

The second command checks the package the way an app developer would use it. It creates a new .NET MAUI app with the installed SDK, restores the package from `artifacts/nuget` into a fresh package folder, shows a PDF, and builds the app.

To check that the Windows code compiles on macOS or Linux, add `-p:IncludeWindowsTfm=true -p:EnableWindowsTargeting=true` to a build of the plugin project. The compile succeeds; a later Windows-only packaging step (`makepri.exe`) fails there.



## Credits and license

MIT. Rendering is done by [Melville.Pdf](https://github.com/DrJohnMelville/Pdf) by John Melville (MIT). The package also bundles code from JpegLibrary (MIT) and the Courier Prime, Linux Libertine, Roboto and Symbola fonts; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), which is included in the package.
