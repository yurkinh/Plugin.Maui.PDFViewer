"""Fail if a package the plugin restates for bundling (BundleMelville) differs from the fork's central version."""
import sys
import xml.etree.ElementTree as ET

project, central = sys.argv[1], sys.argv[2]
versions = {e.get('Include'): e.get('Version') for e in ET.parse(central).getroot().iter('PackageVersion')}
bad = []
checked = 0
for group in ET.parse(project).getroot().iter('ItemGroup'):
    group_bundled = 'BundleMelville' in (group.get('Condition') or '')
    for ref in group.iter('PackageReference'):
        if not group_bundled and 'BundleMelville' not in (ref.get('Condition') or ''):
            continue
        checked += 1
        name, version = ref.get('Include'), ref.get('Version')
        if versions.get(name) != version:
            bad.append(f'{name}: plugin {version}, fork {versions.get(name)}')
if not checked:
    sys.exit('No BundleMelville package references found in ' + project)
if bad:
    sys.exit('Bundled dependency versions differ from external/Pdf/Src/Directory.Packages.props:\n  ' + '\n  '.join(bad))
