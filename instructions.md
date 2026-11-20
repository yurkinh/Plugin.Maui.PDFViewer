# Working on Plugin.Maui.PDFViewer

Everything needed to continue the project from this repository alone. The work started in the Melville.Pdf fork
(`yurkinh/Pdf`) and moved here in phase 11b; the fork is now a submodule you change only when the renderer, viewer
model or text layer needs it.

## 1. What is where

| Path | What |
|---|---|
| `src/Plugin.Maui.PDFViewer` | The plugin: `PdfView`, `PdfSource`, the drawable, the bitmap cache, gestures. Almost all shared code; platform files use the template's suffixes (`*.android.cs`, `*.macios.cs`, `*.windows.cs`). |
| `src/Plugin.Maui.PDFViewer.slnx` | Solution: the plugin plus the fork projects it builds. |
| `tests/Plugin.Maui.PDFViewer.Tests` | Unit tests for `PdfView` on plain `net11.0` (no device). |
| `samples/Plugin.Maui.PDFViewer.Sample` | Minimal template-style sample. |
| `samples/Plugin.Maui.PDFViewer.TestApp` | The full test app: Library (bundled PDFs), Gallery (all reference documents), Diagnostics (timings, smoke test), Checks, Viewer. |
| `samples/Plugin.Maui.PDFViewer.Sample.slnx` | Both samples plus their project references. |
| `external/Pdf` | Submodule: the fork `https://github.com/yurkinh/Pdf`, branch `maui/net11`. |
| `external/Pdf/external/Melville.Streams` | Submodule of the fork: upstream `DrJohnMelville/Melville.Streams`, pristine; local fixes are patches in `external/Pdf/patches`. |
| `pack-nuget.sh` | Packs the single, self-contained NuGet package into `artifacts/nuget`. Never publishes. |
| `tools/test-package-consumer.sh` | Creates a fresh MAUI 11 app, installs the package from `artifacts/nuget`, builds it. |
| `tools/check-package.py`, `tools/check-bundled-versions.py`, `tools/project-closure.py` | Used by `pack-nuget.sh`. |
| `tools/BuildSampleCorpus.cs` | File-based app that regenerates the TestApp's bundled PDFs (`Resources/Raw/samples`). |
| `.github/workflows` | `ci.yml` (build, test, pack on Windows; Native AOT gate on the `xcode-27` image), `ci-sample.yml` (samples on Windows and `xcode-27`), `release-nuget.yml` (tag-driven publish). |
| `src/Plugin.Maui.PDFViewer/README.nuget.md` | The package's readme on nuget.org (packed as `README.md`). |
| `CHANGELOG.md` | Release notes, linked from the package. |
| `THIRD-PARTY-NOTICES.md` | Licenses of bundled code and fonts; packed into the package. |

### Fork projects the plugin uses (in `external/Pdf/Src`)

- `Melville.Pdf.MauiGraphics`: the Maui.Graphics renderer (own display list; `PdfDisplayList`, `PdfPageDrawing`).
- `Melville.Pdf.ViewerModel`: UI-independent viewer state: layout, continuous strip, bitmap plan, links, text, search
  (`TextSearch`), selection (`TextSelection`, `TextHitTest`). Most behaviour lives here and is unit tested here.
- `Melville.Pdf.TextExtractor`: `PageTextLayerAsync`, the positioned text layer (`PageText`, `TextGlyph`).
- `Melville.Pdf.Model`: links (`DocumentRenderer.GetPageLinksAsync`, `PdfLink`), page boxes (`GetPageBoxAsync`).
- `Melville.Pdf.FontLibrary`: the embedded fallback fonts.

## 2. One-time setup (macOS)

```bash
git clone --recursive https://github.com/yurkinh/Plugin.Maui.PDFViewer.git
cd Plugin.Maui.PDFViewer
./external/Pdf/Src/install-dotnet.sh                  # SDK 11.0.100 (global.json) into external/Pdf/Src/.dotnet, + .NET 10 runtime
external/Pdf/Src/.dotnet/dotnet workload install maui # MAUI 11.0.0
./external/Pdf/build-local-packages.sh                # Melville.StreamInterfaces + IntersectionTypes into the fork's local feed
```

- `global.json` here points the SDK at `external/Pdf/Src/.dotnet` first, then the system install, so plain `dotnet`
  works from the repo root once the local SDK exists.
- **Xcode 27.2 is required** for iOS and Mac Catalyst apps (MAUI 11.0.0's iOS 27.2 packs; older Xcode fails with
  MT180, and 27.2 is the only valid iOS platform version). On this Mac it is `/Applications/Xcode-27.2.0.app`, but
  `xcode-select` points at the Command Line Tools: either `sudo xcode-select -s /Applications/Xcode-27.2.0.app` once,
  or prefix Apple builds with `export DEVELOPER_DIR=/Applications/Xcode-27.2.0.app/Contents/Developer`.
- If `dotnet` dies instantly with exit code 137 (no output) after reinstalling an SDK over an existing
  `external/Pdf/Src/.dotnet`, macOS is killing the overwritten launcher. Fix:
  `cd external/Pdf/Src/.dotnet && cp dotnet dotnet.new && mv -f dotnet.new dotnet`.

## 3. Everyday commands (from the repo root)

```bash
dotnet build src/Plugin.Maui.PDFViewer.slnx                       # plugin, all targets (Windows only on Windows)
dotnet test --project $PWD/tests/Plugin.Maui.PDFViewer.Tests/Plugin.Maui.PDFViewer.Tests.csproj   # 50 tests
dotnet build samples/Plugin.Maui.PDFViewer.Sample.slnx           # both samples
./pack-nuget.sh && ./tools/test-package-consumer.sh net11.0-android net11.0-ios net11.0-maccatalyst
```

- `dotnet test` needs an absolute `--project` path (Microsoft.Testing.Platform).
- Fork tests (renderer, view model, data model) run in the fork's `Src` folder:
  ```bash
  cd external/Pdf/Src
  .dotnet/dotnet test --project $PWD/Melville.Pdf.ViewerModel.Tests/Melville.Pdf.ViewerModel.Tests.csproj -p:MelvilleSkipCopyPackage=true   # 210 (net10+net11)
  .dotnet/dotnet test --project $PWD/Melville.Pdf.MauiGraphics.Tests/Melville.Pdf.MauiGraphics.Tests.csproj -p:MelvilleSkipCopyPackage=true # 1,496
  LC_ALL=en_US.UTF-8 .dotnet/dotnet test --project $PWD/Melville.Pdf.DataModelTests/Melville.Pdf.DataModelTests.csproj -p:MelvilleSkipCopyPackage=true
  ```
  Building a single fork project needs `-p:MelvilleSkipCopyPackage=true`. The data-model suite has **24 known
  failing tests on macOS** (45 runs on net10, 43 on net11): PostScript operators, Type 1 charsets, JBIG2,
  cross-reference tables, filters and text extraction that hard-code CRLF or upstream test data. Anything beyond those
  24 names is new.
- Native AOT gate (as CI): `dotnet publish samples/Plugin.Maui.PDFViewer.TestApp/Plugin.Maui.PDFViewer.TestApp.csproj
  -f net11.0-maccatalyst -c Release -p:RuntimeIdentifier=maccatalyst-arm64 -p:MelvilleNativeAot=true
  -p:CreatePackage=false` must show no `warning IL`.
- Windows code compiles on macOS with `-p:IncludeWindowsTfm=true -p:EnableWindowsTargeting=true` on a build of the
  plugin project; the compile succeeds and a later `makepri.exe` step fails (Windows-only tool).

## 4. Build settings that matter (`Directory.Build.props`)

- `PluginTfm` = `net11.0`. **.NET 11 only** (decision 2026-11-12): no net10 targets in the plugin, tests or samples.
  The fork's libraries still build `net10.0;net11.0` for upstream; the package bundles their net11.0 builds.
- `PluginMauiVersion` = `11.0.0`: the `Microsoft.Maui.Controls` version, and so the package's minimum dependency.
  Keep it at the lowest MAUI 11: apps created from the 11.0.0 workload reference 11.0.0, and a higher minimum makes
  their restore fail (NU1605 downgrade). Not `$(MauiVersion)`, so the package doesn't depend on the building workload.
- Runtime async is on (`<Features>runtime-async=on</Features>`); `-p:PluginRuntimeAsync=false` turns it off. It
  needs CoreCLR, which is the .NET 11 default on Android, iOS and Mac Catalyst (`UseMonoRuntime=false`). The fork's
  libraries turn it on for net11.0 in `external/Pdf/Src/Directory.Build.Props` (`-p:MelvilleRuntimeAsync=false`).
- Android minimum API 24 (.NET 11), iOS 15, Mac Catalyst 17, Windows 10.0.17763.

## 5. Changing fork code (renderer, viewer model, text layer)

The submodule tracks `maui/net11` of `yurkinh/Pdf`. To change it:

1. Make the change in a checkout of the fork (or in `external/Pdf` itself), on a branch, with its tests
   (`Melville.Pdf.ViewerModel.Tests` for viewer behaviour, `Melville.Pdf.DataModelTests` for model/text/links).
2. Commit in the fork and **push the fork branch first**: a plugin commit that points at an unpushed fork commit
   cannot be cloned.
3. In this repo: `git -C external/Pdf fetch origin <branch> && git -C external/Pdf checkout FETCH_HEAD`, update
   `branch =` in `.gitmodules` if the branch changed, then commit the submodule pointer here.

Fork rules:
- Leave upstream's `Src/Src.sln` alone (not migrated to .slnx), so merging upstream stays conflict-free. New projects
  are built by explicit project paths in the fork's CI.
- Central package versions are in `external/Pdf/Src/Directory.Packages.props`. MAUI Graphics is conditional:
  10.0.120 for net10.0, 11.0.10 for net11.0 (MAUI 11 packages are net11.0-only).
- Melville.Streams fixes are patches (`external/Pdf/patches/melville-streams-000N-*.patch`), applied by
  `build-local-packages.sh`; the submodule stays on a pristine upstream commit.
- Architecture test: code in `Melville.Pdf.Model.Documents` must not depend on renderers (`LinkReader` lives in
  `Renderers.DocumentRenderers` for that reason).
- New reference documents need `dotnet run tools/ListReferenceGenerators.cs` and a recorded render baseline
  (`MELVILLE_RECORD_BASELINE=1`).

## 6. Packaging and releasing

- **One self-contained package** (decision 2026-11-07). The `Melville.*` IDs on nuget.org belong to the upstream
  author, so the fork's assemblies, `Melville.StreamInterfaces` and `Melville.IntersectionTypes` (not on nuget.org)
  are bundled in `lib/net11.0*`. Packing sets `-p:BundleMelville=true`, which makes the Melville project references
  private and restates their nuget.org dependencies (CoreJ2K 2.3.3.91, Melville.Hacks/INPC/Linq 0.8.5) in the plugin
  project. `pack-nuget.sh` fails if a target lacks a bundled assembly, a dependency is not one of those five
  (with Microsoft.Maui.Controls), or the restated versions drift from the fork's `Directory.Packages.props`.
- Consequence for apps: don't also reference upstream's Melville.Pdf packages (duplicate assemblies).
- CJK fonts are not shipped: the fork's `Melville.Pdf.FontLibrary.Cjk` `.ttc` fonts fail to load (EndOfStream, found
  in phase 0). Apps needing CJK supply their own `IDefaultFontMapper` via `UseMauiPdf(o => o.FontMapper = ...)`.
### Package contents

- The nuget.org readme is `src/Plugin.Maui.PDFViewer/README.nuget.md` (packed as `README.md`): what app developers
  need, absolute links only (nuget.org does not resolve relative ones). The repository `README.md` is the full guide.
  Keep the two in step when the API changes.
- Release notes: `CHANGELOG.md` (linked from `PackageReleaseNotes`). No license acceptance prompt (MIT).
- Only a package packed on **Windows** has all five target frameworks (`net11.0-windows10.0.19041.0` is built only
  there). A package from this Mac has four; publish from CI, never from the Mac.

### Releasing

The release workflow (`release-nuget.yml`) runs for tags `vX.Y.Z`, `vX.Y.Z-previewN` and `vX.Y.Z-preview.N` on a
commit that is on `main`, when the repository variable `PUBLISH_NUGET` is `true` and the secret `NUGET_API_KEY` is set.
On a Windows runner it builds the fork's local packages, runs the tests, packs with the tag's version and pushes
`Plugin.Maui.PDFViewer.*.nupkg` with its `.snupkg` (never the fork's local Melville.Streams packages).

Checklist for the first release (`0.1.0-preview.1`):

1. Windows validated (section 7, "Windows").
2. `main` green: "Build for CI" (Windows build, tests, package; the Native AOT gate) and "Build Sample App for CI"
   (Windows; iOS and Mac Catalyst). The macOS jobs run on the `xcode-27` runner image (macOS 27), which has Xcode
   27.2; `macos-latest` (macOS 26) tops out at Xcode 26.6. Then the Apple device checks in section 7.
3. Download the `nuget-package` artifact of that CI run and check it: five target frameworks, `README.md`,
   `THIRD-PARTY-NOTICES.md`, the icon; optionally `./tools/test-package-consumer.sh` against it.
4. `CHANGELOG.md`: replace "(unreleased)" with the date; commit.
5. GitHub repository settings: variable `PUBLISH_NUGET` = `true`; secret `NUGET_API_KEY`, a nuget.org key with
   "Push new packages and package versions" for the glob `Plugin.Maui.PDFViewer`. (Or move to NuGet trusted
   publishing, OIDC, which needs no long-lived key.) The ID `Plugin.Maui.PDFViewer` was free on 2026-11-17.
6. Tag and push: `git tag v0.1.0-preview.1 && git push origin v0.1.0-preview.1`. The fork commit the submodule points
   at must already be on GitHub.
7. After nuget.org validates the package (minutes): check the readme renders, the symbols are indexed, and
   `dotnet add package Plugin.Maui.PDFViewer --prerelease` works in a new MAUI 11 app. Create a GitHub release for the
   tag with the CHANGELOG entry.

Without a tag, the version is `PluginVersion` in `Directory.Build.props` (currently `0.1.0-preview.1`). Local test
builds reuse that version, so after repacking delete `~/.nuget/packages/plugin.maui.pdfviewer/<version>` (and the
consuming app's `obj`/`bin`) or the app keeps the cached copy.

## 7. Testing on devices

### Android (Motorola Edge 60 Neo, serial `ZY22M6JJFZ`; Android 16, 1200×2670)

```bash
cd samples/Plugin.Maui.PDFViewer.TestApp
dotnet build -t:Install -f net11.0-android -c Release -p:AdbTarget="-s ZY22M6JJFZ"
adb -s ZY22M6JJFZ shell am start -n $(adb -s ZY22M6JJFZ shell cmd package resolve-activity --brief com.melville.pdf.mauisample | tail -1)
```

- Install with `-t:Install`, not `adb install`: Debug builds use fast deployment, so the APK alone carries stale
  assemblies.
- Emulators `emulator-5554` and `emulator-5556` belong to the user: never install on or drive them. The test AVD is
  `MelvillePdfTest` on port 5560.
- Drive the UI with `adb shell input` (taps by coordinates found via `uiautomator dump`). Press-and-hold selection:
  `input motionevent DOWN x y`, wait 1 s, `MOVE` steps, `UP`. DevFlow (Debug builds only, `adb forward tcp:9223
  tcp:9223`) taps are synthetic and carry no position, so they cannot test link hit-testing or selection.
- Unattended diagnostics: `am start ... --ez melville_autorun true` writes `[BENCH]`, `[SMOKE]`, `[AUTORUN]` lines to
  logcat.

### iPhone 13 (UDID `00008110-0019392934EBA01E`)

- Signing: personal team `S5MAYRWNXZ`, identity `Apple Development: yholembyovskyy@gmail.com (UXBCWX7FHH)`,
  profile `iOS Team Provisioning Profile: com.yurkinh.pdfviewer.testapp`. Free-team profiles last 7 days (this one
  until 2026-11-18). To renew, build any Xcode project with that bundle ID and team using
  `xcodebuild -allowProvisioningUpdates -allowProvisioningDeviceRegistration`. Never use the hippo data GmbH
  profiles on this Mac (a client's team), and don't reuse `com.yurkinh.slint` (an installed app of the user's).
- Build, install, run diagnostics:
  ```bash
  export DEVELOPER_DIR=/Applications/Xcode-27.2.0.app/Contents/Developer
  cd samples/Plugin.Maui.PDFViewer.TestApp
  dotnet build -f net11.0-ios -c Release -p:RuntimeIdentifier=ios-arm64 -p:ApplicationId=com.yurkinh.pdfviewer.testapp \
    -p:CodesignKey="Apple Development: yholembyovskyy@gmail.com (UXBCWX7FHH)" \
    -p:CodesignProvision="iOS Team Provisioning Profile: com.yurkinh.pdfviewer.testapp"
  xcrun devicectl device install app --device 00008110-0019392934EBA01E bin/Release/net11.0-ios/ios-arm64/Plugin.Maui.PDFViewer.TestApp.app
  xcrun devicectl device process launch --device 00008110-0019392934EBA01E --terminate-existing --console \
    --environment-variables '{"MELVILLE_AUTORUN":"1"}' com.yurkinh.pdfviewer.testapp
  ```
- Touch gestures cannot be driven from the Mac; ask the user to try press-and-hold selection, Find and links by hand.

### Mac Catalyst

`MELVILLE_AUTORUN=1 <app>/Contents/MacOS/Plugin.Maui.PDFViewer.TestApp` runs the diagnostics and smoke test
unattended (the start line says `dynamic-code=False` for a Native AOT build).

### Windows (automated part and package run 2026-11-18; hand checks still open, see section 10)

One-time setup: .NET 11 SDK 11.0.100 (`global.json` falls back to the system SDK), `dotnet workload install maui`
(or Visual Studio with the .NET MAUI workload), Git for Windows (its bash runs the `.sh` scripts, as CI does). Clone
with `--recursive`. Then, from Git Bash:

```bash
./external/Pdf/build-local-packages.sh
dotnet build src/Plugin.Maui.PDFViewer.slnx                    # now includes net11.0-windows10.0.19041.0
dotnet test --project "$(pwd -W)/tests/Plugin.Maui.PDFViewer.Tests/Plugin.Maui.PDFViewer.Tests.csproj"
dotnet build samples/Plugin.Maui.PDFViewer.Sample/Plugin.Maui.PDFViewer.Sample.csproj -t:Run -f net11.0-windows10.0.19041.0
dotnet build samples/Plugin.Maui.PDFViewer.TestApp/Plugin.Maui.PDFViewer.TestApp.csproj -t:Run -f net11.0-windows10.0.19041.0
./pack-nuget.sh                                                 # must report five target frameworks
```

Check by hand, in the Sample and the TestApp (Library, Gallery, Diagnostics with its smoke test, Viewer):

- Documents open; continuous scroll and single page; fit width and fit page; window resize re-lays out.
- Mouse wheel scrolls, Shift + wheel scrolls sideways, Ctrl + wheel zooms around the pointer; touchpad and
  touch-screen drag (with momentum) and pinch; the scroll indicator appears and fades.
- Double-click zooms; clicking a link goes to its page or opens the browser.
- Toolbar (`ShowToolbar="True"`) and its "⋯" menu; keyboard commands; the find bar, matches and stepping.
- Press and hold selects a word, drag extends it, Copy puts the text on the clipboard.
- Dark theme; Narrator reads the page text; File > Open (`FilePicker`) and an encrypted PDF with the password page.
- Diagnostics smoke test: 251 documents, no failures; note the timings next to the reference results below.

Anything that fails: note it in section 10 with the document and steps.

Building every target also needs the Android SDK platform for API 37 (XA5207 without it), and so does `pack-nuget.sh`.
Python must be a real install: the Microsoft Store `python`/`python3` stubs break `pack-nuget.sh`.

Unattended runs of the TestApp's Windows executable (`bin/<config>/net11.0-windows10.0.19041.0/win-x64`), with stdout
redirected: `MELVILLE_AUTORUN=1` runs the diagnostics and smoke test; `MELVILLE_AUTORUN_CHECKS=1` opens the Checks tab,
which runs the canvas checks and compares every reference document with SkiaSharp (`[FIDELITY]` lines).

Win2D differences, handled in `Rendering/Win2DRendering.windows.cs`: `PlatformBitmapExportService` is not implemented;
`ConcatenateTransform` post-concatenates; a second clip in one saved state throws; clips are intersected with the
canvas size in user coordinates, so they are applied in device coordinates. `SemanticScreenReader.Announce` can throw
`NullReferenceException` on Windows, so `PdfView.Announce` ignores failures.

### Reference results (2026-11-12, Release, .NET 11)

| | Motorola | iPhone 13 | Mac Catalyst |
|---|---|---|---|
| Record a page of text | 60 ms | 62 ms | 34 ms (10 ms Native AOT) |
| Replay into a bitmap | 44 ms | 125 ms (CoreGraphics) | 134 ms |
| Smoke test, 251 documents / 390 pages | 13 s, 0 failures | 11 s, 0 failures | 6 s (2 s Native AOT), 0 failures |
| Search all 120 pages of the long sample | 5 s | not measured | |

.NET 10 on the Motorola for comparison: 260 ms record, 49 s smoke test, 32 s search.

Windows (2026-11-18, Release, x64 desktop): record a page of text 116 ms, smoke test 251 documents / 390 pages in 9 s,
0 failures. Win2D defers drawing until the session ends, so its replay times are not comparable with the table.

## 8. Look and API: following Maui.PDFView

Decision 2026-11-13: the defaults and the API an app touches first follow Maui.PDFView
(`https://github.com/vitalii-vov/Maui.PDFView`, fork at `yurkinh/Maui.PDFView`), so apps can switch easily.

- Defaults: `DisplayMode.Continuous`, `ZoomMode.FitWidth` (double-tap and a new document return to it),
  `ShowToolbar=false`, white behind the pages (`#1E1E1E` dark) unless `BackgroundColor` is set, a soft card shadow.
  A document shorter than the view starts at the top (left when horizontal), as Maui.PDFView's list does.
- Scrolling: momentum after a drag (`Controls/Scrolling.cs`: `FlingTracker`, `Fling`, time constant 325 ms) and a
  fading scroll indicator drawn on the page canvas (`ShowScrollIndicator`). While the view moves, a page's first
  backdrop bitmap is kept even when superseded, so pages do not stay blank during a fling.
- Same names: `MaxZoom`/`MinZoom` (multiples of fit width; fit page stays reachable), `PageChanged` and
  `PageChangedCommand` with `PageChangedEventArgs(CurrentPage, TotalPages)`, `PageAppearance` (`ShadowEnabled`,
  `Margin` default 8,4, `Crop` in PDF points). `Source` takes a string (path, http(s) URL or bundled file).
- Public types are ours (`ZoomMode`, `DisplayMode`, `PdfViewState` in `Controls/PdfViewTypes.cs`, mapped to the
  viewer model's); apps never import `Melville.*` for everyday use. Still Melville: `IPasswordSource` and the font
  mapper option.
- The fork implements zoom limits, page margins and crop in `Melville.Pdf.ViewerModel` (`MinZoom`, `MaxZoom`,
  `PageMargin`, `PageCrop`, `PageInsets`; `PageLayout.OriginX/OriginY` is the page point at a cropped page's corner
  and `Uncropped()` gives the whole page for drawing), and guesses substitute fonts from their names
  (`SubstituteDefaultFont.MapFontName`: "Times New Roman,Bold" becomes Times-Bold, not a sans).
- The toolbar is one row (previous, page box, next, zoom out/in, "⋯" menu with the rest) with an explicit style, so
  the app's implicit Button style does not apply.
- Side-by-side check: build Maui.PDFView's `Example` for `net11.0-android` with this package from `artifacts/nuget`
  and a second Shell tab showing our `PdfView` on the same file.

## 9. Known problems and gotchas

- MAUI 11's XAML source generator fails (MAUIG1001 "not a valid resource path") when the project path goes through a
  symlink, e.g. macOS's `/var` → `/private/var` from `mktemp`. Build from a physical path (`pwd -P`).
- `Color.FromRgba(255, 120, 0, 0.5f)` picks the all-float overload and clamps to 1: use all ints.
- In plain .NET (tests) a MAUI view without a handler never arranges its children; tests lay out recursively.
- A long Release build that seems stuck: check for orphaned `mono-aot-cross`/`opt`/`llc` processes from an earlier
  stopped build (Mono LLVM AOT for net10 iOS could take more than 15 minutes; .NET 11 uses CoreCLR and builds in
  under a minute).
- Text layer glyph boxes are approximated (0.8 em above the baseline, 0.2 em below); text is in drawing order, not
  reading order; hyphenated words are not joined; a selection stays on one page; a screen reader reads a page's text
  in one go.
- Upstream items still open: draft PRs DrJohnMelville/Melville.Streams#1 (MixinGenerator crash on LF checkouts) and
  #2 (ParsingBuffer fix, patch 0004); issue DrJohnMelville/Pdf#45 (optional content without /Intent hidden but drawn).

## 10. Plan

Status 2026-11-17: the plugin is feature complete for a first preview, follows Maui.PDFView's look and API (section 8),
and replaced Maui.PDFView in a production app (retargeted to .NET 11 on a local branch), verified on the Android
emulator (Debug and Release) and the iOS simulator (Debug).

Next, in order:

1. **Windows validation** (section 7, "Windows"). Done 2026-11-18: Windows build, 50 unit tests, both samples build,
   smoke test, 240 reference documents compared with SkiaSharp (no exceptions), `pack-nuget.sh` with five target
   frameworks, and in the Sample: opening, zoom buttons, page buttons, switching documents, window resize. The owner
   finished the hand checks on Windows (2026-11-20) and merged the fixes (PR #1).
   - Known difference: Win2D clips a miter join that exceeds the miter limit instead of bevelling it, so very sharp
     corners overshoot (reference document `WpfLineRenderBug`, 6% of its pixels).
2. **Apple checks**: the Native AOT gate locally; on the iPhone, press-and-hold selection and Copy, the find bar,
   tapping links, momentum scrolling and the scroll indicator (Android was verified).
3. **First release** `0.1.0-preview.1` (section 6, "Releasing"), 2026-11-20.
4. After the preview, as feedback comes in:
   - A draggable scroll thumb (needs platform touch handling: MAUI's pan gesture does not report where a drag starts).
   - The find and selection bars in the toolbar's flat style; left-aligned items in the "⋯" menu.
   - Public types still from Melville: `IPasswordSource`, the font mapper option.
   - TalkBack/VoiceOver/Narrator runs; memory-pressure handling; CJK fonts (section 6).
   - The intermittent fork test `ANewPageStartsAtTheTopLeft` (checks the scroll just before the reset; a test timing
     issue, not the layout).
