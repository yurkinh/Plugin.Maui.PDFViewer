#!/usr/bin/env bash
# Check the packed Plugin.Maui.PDFViewer works for an app developer: create a fresh .NET MAUI app with the installed
# (non-repository) SDK, restore Plugin.Maui.PDFViewer from artifacts/nuget, show a bundled PDF in a PdfView, and build it.
#
#   ./pack-nuget.sh && ./tools/test-package-consumer.sh [net11.0-android net11.0-ios ...]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FEED="$ROOT/artifacts/nuget"
VERSION="$(ls "$FEED" | sed -n 's/^Plugin\.Maui\.PDFViewer\.\([0-9].*\)\.nupkg$/\1/p' | head -1)"
[ -n "$VERSION" ] || { echo "No Plugin.Maui.PDFViewer package in $FEED; run ./pack-nuget.sh first." >&2; exit 1; }
FRAMEWORKS=("${@:-net11.0-android}")
DOTNET="${CONSUMER_DOTNET:-dotnet}"      # deliberately not the repository SDK: a consumer has a normal one
# The physical path: on macOS mktemp gives /var/..., a symlink to /private/var/..., and MAUI 11's XAML source
# generator then rejects the template's resource paths (MAUIG1001 "not a valid resource path").
WORK="$(cd "$(mktemp -d)" && pwd -P)"; trap 'status=$?; rm -rf "$WORK"; exit $status' EXIT
cd "$WORK"
# Its own package folder: a package of the same version cached from an earlier pack must not stand in for this one.
export NUGET_PACKAGES="$WORK/packages"

"$DOTNET" new maui -n PdfConsumer -o . -f net11.0 >/dev/null   # the package targets .NET 11 only
cat > nuget.config <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$FEED" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML
python3 - "$VERSION" <<'PY'
import sys
version = sys.argv[1]
p = 'PdfConsumer.csproj'; s = open(p, encoding='utf-8-sig').read()
s = s.replace('<PackageReference Include="Microsoft.Maui.Controls" Version="$(MauiVersion)" />',
              '<PackageReference Include="Microsoft.Maui.Controls" Version="$(MauiVersion)" />\n'
              f'    <PackageReference Include="Plugin.Maui.PDFViewer" Version="{version}" />')
open(p, 'w').write(s)
p = 'MauiProgram.cs'; s = open(p, encoding='utf-8-sig').read()
open(p, 'w').write('using Plugin.Maui.PDFViewer;\n' + s.replace('.UseMauiApp<App>()', '.UseMauiApp<App>()\n\t\t\t.UseMauiPdf()'))
open('MainPage.xaml', 'w').write('''<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:pdf="https://github.com/yurkinh/Plugin.Maui.PDFViewer"
             x:Class="PdfConsumer.MainPage">
    <pdf:PdfView x:Name="Viewer" />
</ContentPage>
''')
open('MainPage.xaml.cs', 'w').write('''using Plugin.Maui.PDFViewer;

namespace PdfConsumer;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        Viewer.Source = PdfSource.FromAppPackage("hello.pdf");
    }
}
''')
PY
cp "$ROOT/samples/Plugin.Maui.PDFViewer.Sample/Resources/Raw/layers.pdf" Resources/Raw/hello.pdf

for tfm in "${FRAMEWORKS[@]}"; do
  rid=""
  if [[ $tfm == *-ios ]]; then rid="-p:RuntimeIdentifier=iossimulator-arm64"; fi  # simulator: no signing
  echo "== $tfm (Plugin.Maui.PDFViewer $VERSION)"
  "$DOTNET" build -f "$tfm" -p:TargetFrameworks="$tfm" $rid -nologo -v:quiet
done
echo "Consumer app built against Plugin.Maui.PDFViewer $VERSION."
