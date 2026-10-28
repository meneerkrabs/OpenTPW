#!/usr/bin/env python3
"""Qualify the PE audit using tiny generated assemblies, never game/media fixtures.

Run only in an authorized SDK environment. No browser, assembly loading, publishing,
Git writes, or artifact upload occurs here. This is NOT renderer coverage.
"""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET


def run(args):
    root = Path(args.work_root).absolute()
    if root.exists():
        raise ValueError('self-test work root must be fresh')
    root.mkdir(parents=True)
    repository = Path(args.repository_root).absolute()
    auditor = Path(args.auditor).absolute()
    allowlist = repository / 'tools/web-path-render/artifact-allowlist.json'
    pins = json.loads(allowlist.read_text())
    generated = root / 'generated'
    generated.mkdir()
    builds = 0

    def build(name, resources=(), extra=False, corrupt=False):
        nonlocal builds
        builds += 1
        project_root = generated / str(builds)
        project_root.mkdir()
        project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
        properties = ET.SubElement(project, 'PropertyGroup')
        for key, value in {'TargetFramework': 'net11.0', 'AssemblyName': name,
                           'EnableDefaultCompileItems': 'false', 'DebugType': 'None',
                           'EnableDefaultEmbeddedResourceItems': 'false', 'EnableDefaultNoneItems': 'false'}.items():
            ET.SubElement(properties, key).text = value
        items = ET.SubElement(project, 'ItemGroup')
        ET.SubElement(items, 'Compile', Include='Fixture.cs')
        (project_root / 'Fixture.cs').write_text('public static class SyntheticAuditFixture { }\n')
        for index, item in enumerate(resources):
            source = repository / item['path']
            if corrupt and index == 0:
                source = project_root / 'wrong-shader.txt'
                source.write_text('synthetic wrong shader; intentionally not its pin\n')
            ET.SubElement(items, 'EmbeddedResource', Include=str(source), LogicalName=item['logicalName'])
        if extra:
            (project_root / 'extra.txt').write_text('synthetic forbidden resource; no original media\n')
            ET.SubElement(items, 'EmbeddedResource', Include='extra.txt', LogicalName='synthetic.forbidden')
        project_file = project_root / 'Fixture.csproj'
        ET.ElementTree(project).write(project_file, encoding='unicode')
        output = root / 'compiled' / str(builds)
        subprocess.run(['dotnet', 'build', str(project_file), '-c', 'Release', '--nologo', '-v', 'q',
                        '--artifacts-path', str(root / 'build' / str(builds)), '-o', str(output)], check=True)
        return output / (name + '.dll')

    baseline = root / 'baseline'
    framework = baseline / '_framework'
    framework.mkdir(parents=True)
    for name in ['OpenTPW.Web', 'OpenTPW.Online', 'OpenTPW.Web.Veldrid']:
        dll = build(name, pins['shaders'] if name == 'OpenTPW.Web' else [])
        shutil.copyfile(dll, framework / dll.name)

    def case(name, edit=None, expected=None):
        web = root / 'cases' / name
        shutil.copytree(baseline, web)
        if edit:
            edit(web / '_framework')
        report_path = root / 'reports' / (name + '.json')
        result = subprocess.run(['dotnet', str(auditor), '--web-root', str(web),
            '--repository-root', str(repository), '--allowlist', str(allowlist), '--output', str(report_path)],
            capture_output=True, text=True)
        if not report_path.exists():
            raise AssertionError(f'{name}: audit did not write a report: {result.stderr}')
        report = json.loads(report_path.read_text())
        if expected is None:
            if result.returncode != 0 or report.get('passed') is not True or report.get('errors') != []:
                raise AssertionError(f'{name}: positive fixture failed: {result.stderr}')
        elif result.returncode == 0 or report.get('passed') is not False or not any(expected in error for error in report.get('errors', [])):
            raise AssertionError(f'{name}: expected rejection containing {expected!r}, got {report}')
        print(f'PE audit fixture PASS: {name}')

    case('exact-four-shaders')
    case('missing-project', lambda directory: (directory / 'OpenTPW.Web.dll').unlink(), 'Expected exactly one')
    case('duplicate-identity', lambda directory: shutil.copyfile(directory / 'OpenTPW.Web.dll', directory / 'duplicate.dll'), 'Duplicate final assembly')
    case('malformed-dll', lambda directory: (directory / 'malformed.dll').write_bytes(b'synthetic-not-a-PE'), 'BadImageFormatException')
    case('symlink', lambda directory: (directory / 'linked.dll').symlink_to(directory / 'OpenTPW.Web.dll'), 'Links are forbidden')
    extra = build('OpenTPW.Web', pins['shaders'], extra=True)
    case('extra-resource', lambda directory: shutil.copyfile(extra, directory / 'OpenTPW.Web.dll'), 'Unexpected Web embedded resource')
    corrupt = build('OpenTPW.Web', pins['shaders'], corrupt=True)
    case('wrong-resource-bytes', lambda directory: shutil.copyfile(corrupt, directory / 'OpenTPW.Web.dll'), 'differs from pinned')
    unexpected = build('OpenTPW.UnexpectedFixture')
    case('extra-project', lambda directory: shutil.copyfile(unexpected, directory / unexpected.name), 'Unexpected OpenTPW assembly')
    tests = build('MSTest.SyntheticFixture')
    case('test-framework', lambda directory: shutil.copyfile(tests, directory / tests.name), 'Test/framework assembly is forbidden')
    print('All 9 actual PE audit fixture cases passed. No renderer/browser was exercised.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['auditor', 'repository-root', 'work-root']:
        parser.add_argument('--' + name, required=True)
    run(parser.parse_args())
