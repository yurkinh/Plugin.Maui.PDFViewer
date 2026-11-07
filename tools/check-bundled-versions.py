"""Fail if a package the plugin restates for bundling (BundleMelville) differs from the fork's central version."""
import sys
import xml.etree.ElementTree as ET

project, central = sys.argv[1], sys.argv[2]
versions = {e.get('Include'): e.get('Version') for e in ET.parse(central).getroot().iter('PackageVersion')}
bad = []
for group in ET.parse(project).getroot().iter('ItemGroup'):
    if 'BundleMelville' not in (group.get('Condition') or ''):
        continue
    for ref in group.iter('PackageReference'):
        name, version = ref.get('Include'), ref.get('Version')
        if versions.get(name) != version:
            bad.append(f'{name}: plugin {version}, fork {versions.get(name)}')
if bad:
    sys.exit('Bundled dependency versions differ from external/Pdf/Src/Directory.Packages.props:\n  ' + '\n  '.join(bad))
