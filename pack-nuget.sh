#!/usr/bin/env bash
# Pack Plugin.Maui.PDFViewer into artifacts/nuget. Nothing is published.
#
#   ./pack-nuget.sh                        # version from Directory.Build.props
#   PLUGIN_VERSION=0.2.0 ./pack-nuget.sh
#
# The package targets .NET 11 only and is self-contained: the Melville.Pdf assemblies it uses are built from the fork in
# external/Pdf (which adds the MAUI renderer, viewer model, links and text layer to upstream) and bundled in its lib
# folders, because the Melville.* IDs on nuget.org belong to the upstream author. So are Melville.StreamInterfaces and Melville.IntersectionTypes, built from
# the fork's Melville.Streams submodule and not on nuget.org. Its dependencies are packages on nuget.org only.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
FORK="$ROOT/external/Pdf"
OUT="$ROOT/artifacts/nuget"
PROJECT="$ROOT/src/Plugin.Maui.PDFViewer/Plugin.Maui.PDFViewer.csproj"
MELVILLE_VERSION="${MELVILLE_VERSION:-0.6.9-maui.1}"   # stamps the bundled assemblies
DOTNET="$FORK/Src/.dotnet/dotnet"; [ -x "$DOTNET" ] || DOTNET=dotnet
PYTHON="$(command -v python3 || command -v python)"   # Windows runners have "python"
PLUGIN_VERSION_ARG=()
[ -n "${PLUGIN_VERSION:-}" ] && PLUGIN_VERSION_ARG=(-p:PluginVersion="$PLUGIN_VERSION")

# The packages the plugin restates for the bundled projects must be the versions the fork builds with.
"$PYTHON" "$ROOT/tools/check-bundled-versions.py" "$PROJECT" "$FORK/Src/Directory.Packages.props"

"$FORK/build-local-packages.sh"   # Melville.StreamInterfaces and Melville.IntersectionTypes, for restore
rm -rf "$OUT"; mkdir -p "$OUT"

cd "$ROOT"   # the plugin's global.json and nuget.config apply
"$DOTNET" pack "$PROJECT" -c Release -o "$OUT" \
  -p:BundleMelville=true -p:MelvilleVersion="$MELVILLE_VERSION" -p:MelvilleSkipCopyPackage=true \
  -p:ContinuousIntegrationBuild=true ${PLUGIN_VERSION_ARG[@]+"${PLUGIN_VERSION_ARG[@]}"} -nologo -v:quiet

# A bundling mistake would only show when someone installs the package: check its contents now.
"$PYTHON" "$ROOT/tools/check-package.py" "$OUT"
echo
ls -1 "$OUT"
