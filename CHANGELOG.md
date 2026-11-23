# Changelog

## 1.0.0 (2026-11-23)

The first stable release. The API is the same as in 0.1.0-preview.1.

- Requires Microsoft.Maui.Controls 11.0.10 or later (was 11.0.0). Apps created from the MAUI 11.0.0 template must raise
  their `Microsoft.Maui.Controls` reference to 11.0.10.
- CoreJ2K 2.4.2.112 (was 2.3.3.91) for JPEG 2000 images.

## 0.1.0-preview.1 (2026-11-20)

First preview of `PdfView` for .NET MAUI 11 (.NET 11 only).

- Pages rendered in pure .NET by Melville.Pdf and drawn with Microsoft.Maui.Graphics on Android, iOS, Mac Catalyst
  and Windows; trim and Native AOT compatible.
- Continuous (default) or single-page display, vertical or horizontal; fit width (default) or fit page.
- Pinch and double-tap zoom with `MinZoom`/`MaxZoom`; momentum scrolling and a fading scroll indicator.
- Search with highlights, text selection and copy, links within the document and to web and mail addresses.
- Optional compact toolbar; public commands for custom UI.
- `PageAppearance`: shadow, margins, crop.
- Encrypted documents (`IPasswordSource`), layers (optional content), `ViewState` to save and restore the position.
- Sources: files, URLs, app package files, embedded resources, `FilePicker` results, bytes and streams; strings in
  XAML and bindings.
- Events and commands: `PageChanged`/`PageChangedCommand`, `DocumentLoaded`, `LoadFailed`, `LinkTapped`.
- Screen reader support for page text and controls.
- Windows: pages drawn with Win2D through a dedicated canvas and bitmap path (Maui.Graphics' Windows bitmap export
  is not implemented, and Win2D differs in transforms and clipping).
