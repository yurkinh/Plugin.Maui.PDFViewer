"""Check the packed Plugin.Maui.PDFViewer: every target framework carries the Melville assemblies, and no dependency
is a package that is not on nuget.org (the Melville.* builds of the fork, Melville.StreamInterfaces)."""
import glob
import re
import sys
import zipfile

REQUIRED = ['Plugin.Maui.PDFViewer', 'Melville.Pdf.Model', 'Melville.Pdf.LowLevel', 'Melville.Parsing',
            'Melville.Pdf.MauiGraphics', 'Melville.Pdf.ViewerModel', 'Melville.Pdf.TextExtractor',
            'Melville.Pdf.FontLibrary', 'Melville.StreamInterfaces', 'Melville.IntersectionTypes']
ALLOWED_DEPENDENCIES = {'Microsoft.Maui.Controls', 'CoreJ2K', 'Melville.Hacks', 'Melville.INPC', 'Melville.Linq'}

out = sys.argv[1]
packages = [p for p in glob.glob(f'{out}/Plugin.Maui.PDFViewer.*.nupkg') if not p.endswith('.snupkg')]
if len(packages) != 1:
    sys.exit(f'expected one Plugin.Maui.PDFViewer package in {out}, found {packages}')
with zipfile.ZipFile(packages[0]) as package:
    names = package.namelist()
    nuspec = package.read(next(n for n in names if n.endswith('.nuspec'))).decode()
tfms = sorted({n.split('/')[1] for n in names if n.startswith('lib/')})
problems = []
for tfm in tfms:
    present = {n.split('/')[-1][:-4] for n in names if n.startswith(f'lib/{tfm}/') and n.endswith('.dll')}
    problems += [f'lib/{tfm}: {name}.dll missing' for name in REQUIRED if name not in present]
dependencies = set(re.findall(r'<dependency id="([^"]+)"', nuspec))
problems += [f'dependency on {d} (not on nuget.org, or should be bundled)' for d in sorted(dependencies - ALLOWED_DEPENDENCIES)]
if problems:
    sys.exit('Package check failed:\n  ' + '\n  '.join(problems))
print(f'{packages[0]}: {len(tfms)} target frameworks ({", ".join(tfms)}), dependencies: {", ".join(sorted(dependencies))}')
