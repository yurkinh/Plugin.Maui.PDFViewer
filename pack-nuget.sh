#!/usr/bin/env bash
# Pack Plugin.Maui.PDFViewer and the Melville.Pdf libraries it depends on into artifacts/nuget. Nothing is published.
#
#   ./pack-nuget.sh                                  # versions from Directory.Build.props
#   PLUGIN_VERSION=0.2.0 MELVILLE_VERSION=0.6.9-maui.2 ./pack-nuget.sh
#
# The Melville.Pdf libraries come from the fork in external/Pdf (net10.0 and net11.0; upstream's packages are
# net11.0 only) and are stamped with MELVILLE_VERSION so they never collide with upstream's. Melville.StreamInterfaces
# is built from the fork's Melville.Streams submodule.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
FORK="$ROOT/external/Pdf"
OUT="$ROOT/artifacts/nuget"
MELVILLE_VERSION="${MELVILLE_VERSION:-0.6.9-maui.1}"
DOTNET="$FORK/Src/.dotnet/dotnet"; [ -x "$DOTNET" ] || DOTNET=dotnet
PYTHON="$(command -v python3 || command -v python)"   # Windows runners have "python"
PLUGIN_VERSION_ARG=()
[ -n "${PLUGIN_VERSION:-}" ] && PLUGIN_VERSION_ARG=(-p:PluginVersion="$PLUGIN_VERSION")

"$FORK/build-local-packages.sh"
rm -rf "$OUT"; mkdir -p "$OUT"
cp "$FORK"/packages/*.nupkg "$OUT"/

# The plugin and every project it references (not analyzers or source generators), dependencies first.
PROJECTS=()
while IFS= read -r line; do PROJECTS+=("$line"); done < <("$PYTHON" "$ROOT/tools/project-closure.py" "$ROOT/src/Plugin.Maui.PDFViewer/Plugin.Maui.PDFViewer.csproj")

cd "$ROOT"   # the plugin's global.json and nuget.config apply to every pack
for project in "${PROJECTS[@]}"; do
  echo "== $(basename "$project")"
  "$DOTNET" pack "$project" -c Release -o "$OUT" \
    -p:MelvilleVersion="$MELVILLE_VERSION" -p:MelvilleSkipCopyPackage=true -p:ContinuousIntegrationBuild=true \
    -p:GeneratePackageOnBuild=false ${PLUGIN_VERSION_ARG[@]+"${PLUGIN_VERSION_ARG[@]}"} -nologo -v:quiet
done
echo
ls -1 "$OUT"
