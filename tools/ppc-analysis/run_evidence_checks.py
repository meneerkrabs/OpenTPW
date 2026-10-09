"""Run every ppc-analysis Python suite and the standalone .NET self-test harnesses, one lane at a time.

Usage: python3 -I tools/ppc-analysis/run_evidence_checks.py [--repo CHECKOUT] [--mac-bin FERAL_BIN]
           [--pc-data TPW_DATA] [--require-fixtures] [--dotnet DOTNET_EXE] [--json]

CI's `unittest discover -s tools/ppc-analysis` reaches only the top-level test files: lane
directories have no __init__.py, and several lanes import a module named `evidence`, so they cannot
share one interpreter either. This runner starts one discovery process per directory and fails if
any test file under tools/ppc-analysis is not covered by exactly one of them.

Fixture variables are set explicitly from the flags and stripped from the inherited environment
otherwise, so a stale shell variable cannot change the result. --mac-bin is the Feral `bin`
directory; it sets OPENTPW_PPC_BIN_ROOT to that directory and OPENTPW_MAC_APP to the
SimThemePark.data file in it (rides wants the file, the other lanes the directory). Only those two
paths are stat'ed; no directory is listed. UI_EVIDENCE_* variables pass through unchanged.
--require-fixtures turns any skipped test into a failure.

--dotnet runs the lane harnesses that have a synthetic self-test, each from a temporary copy so
that no global.json applies and no bin/obj lands in the checkout. Harnesses that need a private
corpus are reported, not run. A harness whose project reference targets a newer framework than
itself is reported as an incompatible reference and not run. Failures are classified as restore
(NU1100: targeting pack not in the cache and not downloadable), missing runtime, SDK too old, or
incompatible reference. Passing harnesses are self-consistency checks only, not evidence of
original runtime behavior.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

FIXTURE_VARIABLES = ('OPENTPW_PPC_BIN_ROOT', 'OPENTPW_MAC_APP', 'OPENTPW_PC_DATA')
APP_NAME = 'SimThemePark.data'

# Lane harness -> self-test arguments. Anything not listed here is reported, not run.
SELF_TESTS = {
    'lanes/advisor/OriginalAdvisorLipDriver.csproj': [],
    'lanes/economy/OriginalLoanRules.Tests.csproj': [],
    'lanes/guests/rules/GuestOriginalRules.Tests.csproj': [],
    'lanes/clock/OriginalSchedulerRules/OriginalSchedulerRules.csproj': [],
    'lanes/rides/animation/AnimationWitness.csproj': [],
    'lanes/ui/csharp/OriginalLayoutReader.csproj': ['--self-test'],
}
NEEDS_CORPUS = {
    'lanes/advisor/layer1/Layer1Corpus.csproj': 'Layer I corpus comparison (private corpus, long run)',
    'lanes/rides/CorpusWitness.csproj': 'PC data witness (private install)',
}


class FixtureError(Exception):
    pass


def fixture_environment(base: dict, mac_bin: Path | None, pc_data: Path | None) -> dict:
    env = {key: value for key, value in base.items() if key not in FIXTURE_VARIABLES}
    if mac_bin is not None:
        app = mac_bin / APP_NAME
        if not mac_bin.is_dir():
            raise FixtureError(f'--mac-bin is not a directory: {mac_bin}')
        if not app.is_file():
            raise FixtureError(f'--mac-bin has no {APP_NAME} data fork (MacBinary .bin does not pass identity)')
        env['OPENTPW_PPC_BIN_ROOT'] = str(mac_bin)
        env['OPENTPW_MAC_APP'] = str(app)
    if pc_data is not None:
        if not pc_data.is_dir():
            raise FixtureError(f'--pc-data is not a directory: {pc_data}')
        env['OPENTPW_PC_DATA'] = str(pc_data)
    return env


def python_suites(tools: Path) -> list[Path]:
    lanes = sorted(path for path in (tools / 'lanes').iterdir() if path.is_dir() and any(path.glob('test_*.py')))
    return [tools, *lanes]


def uncovered_test_files(tools: Path, suites: list[Path]) -> list[str]:
    # Without __init__.py, discovery reaches only test files directly inside the start directory.
    covered = {file for suite in suites for file in suite.glob('test_*.py')}
    every = {file for file in tools.rglob('test_*.py') if '__pycache__' not in file.parts}
    return sorted(str(file.relative_to(tools)) for file in every - covered)


def parse_unittest(output: str) -> dict:
    ran = re.search(r'^Ran (\d+) tests?', output, re.M)
    status = re.search(r'^(OK|FAILED)(?: \((.*)\))?$', output, re.M)
    counts = dict(re.findall(r'(\w+)=(\d+)', status.group(2) or '')) if status else {}
    return {
        'ran': int(ran.group(1)) if ran else 0,
        'ok': bool(status) and status.group(1) == 'OK',
        'skipped': int(counts.get('skipped', 0)),
        'failures': int(counts.get('failures', 0)) + int(counts.get('errors', 0)),
        'skip_reasons': sorted(set(re.findall(r"\.\.\. skipped '([^']*)'", output))),
    }


def run_python(suite: Path, tools: Path, env: dict, require_fixtures: bool) -> dict:
    process = subprocess.run([sys.executable, '-I', '-B', '-m', 'unittest', 'discover', '-s', str(suite),
                              '-t', str(suite), '-p', 'test_*.py', '-v'],
                             env=env, cwd=suite, capture_output=True, text=True, timeout=600)
    result = parse_unittest(process.stderr)
    result['suite'] = str(suite.relative_to(tools)) if suite != tools else '.'
    result['passed'] = (process.returncode == 0 and result['ok'] and result['ran'] > 0
                        and not (require_fixtures and result['skipped']))
    return result


def classify_dotnet_failure(output: str) -> str:
    if re.search(r'NU1201|cannot be referenced by a project that targets', output):
        return 'incompatible-reference'
    if 'NETSDK1045' in output:
        return 'sdk-too-old'
    if 'NU1100' in output:
        return 'restore-targeting-pack-unavailable'
    if re.search(r'framework .Microsoft\.NETCore\.App.*not found|You must install or update \.NET|To install missing framework', output, re.S):
        return 'missing-runtime'
    if 'sdk-not-found' in output or 'A compatible .NET SDK was not found' in output:
        return 'global-json-sdk-not-found'
    return 'failed'


def project_framework(project: Path) -> str | None:
    match = re.search(r'<TargetFramework>([^<]+)</TargetFramework>', project.read_text(encoding='utf-8'))
    return match.group(1).strip() if match else None


def framework_major(framework: str | None) -> int:
    match = re.fullmatch(r'net(\d+)\.\d+', framework or '')
    return int(match.group(1)) if match else 0


def incompatible_references(project: Path) -> list[str]:
    text = project.read_text(encoding='utf-8')
    own = framework_major(project_framework(project))
    found = []
    for include in re.findall(r'<ProjectReference\s+Include="([^"]+)"', text):
        reference = (project.parent / include.replace('\\', '/')).resolve()
        if reference.is_file() and framework_major(project_framework(reference)) > own:
            found.append(f'{include} targets {project_framework(reference)}')
    return found


def dotnet_harnesses(tools: Path) -> list[tuple[str, Path]]:
    return sorted((str(path.relative_to(tools)), path) for path in (tools / 'lanes').rglob('*.csproj')
                  if not {'bin', 'obj'} & set(path.parts))


def run_dotnet(dotnet: Path, key: str, project: Path) -> dict:
    result = {'project': key, 'framework': project_framework(project)}
    incompatible = incompatible_references(project)
    if incompatible:
        return {**result, 'status': 'incompatible-reference', 'reason': '; '.join(incompatible)}
    if key in NEEDS_CORPUS:
        return {**result, 'status': 'not-run', 'reason': NEEDS_CORPUS[key]}
    if key not in SELF_TESTS:
        return {**result, 'status': 'not-run', 'reason': 'no registered self-test'}
    env = {**os.environ, 'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1',
           'DOTNET_ROOT': str(dotnet.resolve().parent)}
    with tempfile.TemporaryDirectory(prefix='ppc-harness-') as scratch:
        copy = Path(scratch) / project.parent.name
        shutil.copytree(project.parent, copy, ignore=shutil.ignore_patterns('bin', 'obj'))
        version = subprocess.run([str(dotnet), '--version'], cwd=scratch, env=env,
                                 capture_output=True, text=True, timeout=60).stdout.strip()
        process = subprocess.run([str(dotnet), 'run', '--project', str(copy / project.name), '--', *SELF_TESTS[key]],
                                 cwd=scratch, env=env, capture_output=True, text=True, timeout=600)
    output = process.stdout + process.stderr
    lines = [line for line in process.stdout.splitlines() if line.strip()]
    status = 'passed' if process.returncode == 0 else classify_dotnet_failure(output)
    return {**result, 'sdk': version, 'status': status, 'last_line': lines[-1] if lines else ''}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--mac-bin', type=Path)
    parser.add_argument('--pc-data', type=Path)
    parser.add_argument('--require-fixtures', action='store_true')
    parser.add_argument('--dotnet', type=Path)
    parser.add_argument('--json', action='store_true')
    args = parser.parse_args(argv)

    tools = args.repo.resolve() / 'tools' / 'ppc-analysis'
    try:
        env = fixture_environment(dict(os.environ), args.mac_bin, args.pc_data)
    except FixtureError as error:
        print(f'fixture error: {error}', file=sys.stderr)
        return 2
    suites = python_suites(tools)
    report = {
        'fixtures': {key: env.get(key) for key in FIXTURE_VARIABLES},
        'uncovered': uncovered_test_files(tools, suites),
        'python': [run_python(suite, tools, env, args.require_fixtures) for suite in suites],
        'dotnet': [run_dotnet(args.dotnet, key, path) for key, path in dotnet_harnesses(tools)] if args.dotnet else [],
    }
    failed = (bool(report['uncovered']) or any(not suite['passed'] for suite in report['python'])
              or any(item['status'] not in ('passed', 'not-run') for item in report['dotnet']))

    if args.json:
        print(json.dumps(report, indent=2))
    else:
        for suite in report['python']:
            mark = 'ok  ' if suite['passed'] else 'FAIL'
            print(f"{mark} python {suite['suite']}: {suite['ran']} ran, {suite['skipped']} skipped, {suite['failures']} failed")
            for reason in suite['skip_reasons']:
                print(f'       skip: {reason}')
        for file in report['uncovered']:
            print(f'FAIL uncovered test file: {file}')
        for item in report['dotnet']:
            mark = 'ok  ' if item['status'] in ('passed', 'not-run') else 'FAIL'
            detail = item.get('last_line') or item.get('reason', '')
            print(f"{mark} dotnet {item['project']} ({item['framework']}, sdk {item.get('sdk', '-')}): {item['status']} {detail}")
        total = sum(suite['ran'] for suite in report['python'])
        skipped = sum(suite['skipped'] for suite in report['python'])
        print(f"{'FAILED' if failed else 'OK'}: {len(suites)} Python suites, {total} tests, {skipped} skipped")
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
