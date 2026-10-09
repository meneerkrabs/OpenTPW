"""Run every ppc-analysis Python suite and the standalone .NET self-test harnesses, one lane at a time.

Usage: python3 -I tools/ppc-analysis/run_evidence_checks.py [--repo CHECKOUT] [--mac-bin FERAL_BIN]
           [--pc-data TPW_DATA] [--pc-fixture EASYMODE_TPWI] [--require-fixtures] [--dotnet DOTNET_EXE] [--json]

CI's `unittest discover -s tools/ppc-analysis` reaches only the top-level test files: lane
directories have no __init__.py, and several lanes import a module named `evidence`, so they cannot
share one interpreter either. This runner starts one discovery process per directory and fails if
any test file under tools/ppc-analysis is not covered by exactly one of them.

Fixture variables are set explicitly from the flags and stripped from the inherited environment
otherwise, so a stale shell variable cannot change the result. --mac-bin is the Feral `bin`
directory; it sets OPENTPW_PPC_BIN_ROOT to that directory and OPENTPW_MAC_APP to the
SimThemePark.data file in it (rides wants the file, the other lanes the directory). Only those two
paths are stat'ed; no directory is listed. --pc-fixture is the one identified PC save container
(Data/levels/jungle/Easymode.TPWI); it sets OPENTPW_PC_FIXTURE and OPENTPW_PPC_SAVE_PATH (clock's
name for the same save) and is passed as `--fixture` to
harnesses registered in FIXTURE_ARGUMENTS. It is never derived from --pc-data. The runner checks only
its shape (a regular .TPWI file outside the checkout, longer than the container header, within the
8 MiB harness bound); the lanes check its identity. UI_EVIDENCE_* variables pass through unchanged.

Absent fixtures never fail by themselves. --require-fixtures turns any skipped test, any harness
"NOT RUN:" fixture line, and any supplied fixture flag none of whose variables was actually used
into a failure. A variable is used when a ppc-analysis Python source reads it (every Python suite
runs) or when a registered harness passed with its fixture argument and printed no "NOT RUN:" line.
A harness that is present but not run (no --dotnet), absent, skipped or failed is not a consumer.

--dotnet runs the lane harnesses that have a synthetic self-test, each from a temporary copy so
that no global.json applies and no bin/obj lands in the checkout. Harnesses that need a private
corpus, or that have no registered self-test, are reported, not run; registered harnesses missing
from the checkout are reported as absent, so coverage is never claimed for them. A harness whose
project reference targets a newer framework than itself is reported as an incompatible reference
and not run. The scratch copy holds only the project directory, so a registered harness with an
item that climbs out of it is reported as linked-outside-copy and not built; an unregistered one
names those items in its not-run reason. Failures are classified as restore (NU1100: targeting
pack not in the cache and not downloadable), missing runtime, SDK too old, or incompatible
reference. Passing harnesses are self-consistency checks only, not evidence of
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

FIXTURE_VARIABLES = ('OPENTPW_PPC_BIN_ROOT', 'OPENTPW_MAC_APP', 'OPENTPW_PC_DATA', 'OPENTPW_PC_FIXTURE',
                     'OPENTPW_PPC_SAVE_PATH')
ITEM = re.compile(r'<(Compile|ProjectReference|None|Content|EmbeddedResource)\s+[^>]*?Include="([^"]+)"')
PROJECT_DIRECTORY = re.compile(r'^\$\((?:MSBuildThisFileDirectory|MSBuildProjectDirectory)\)[\\/]?')
FIXTURE_FLAGS = {
    '--mac-bin': ('OPENTPW_PPC_BIN_ROOT', 'OPENTPW_MAC_APP'),
    '--pc-data': ('OPENTPW_PC_DATA',),
    '--pc-fixture': ('OPENTPW_PC_FIXTURE', 'OPENTPW_PPC_SAVE_PATH'),
}
APP_NAME = 'SimThemePark.data'
PC_FIXTURE_SUFFIX = '.tpwi'
PC_FIXTURE_HEADER_BYTES = 0x629  # zlib payload starts here in the identified container
PC_FIXTURE_MAX_BYTES = 8 * 1024 * 1024  # OriginalScientistSnapshotReader.MaximumPayloadBytes

# Lane harness -> self-test arguments. Anything not listed here is reported, not run.
SELF_TESTS = {
    'lanes/advisor/OriginalAdvisorLipDriver.csproj': [],
    'lanes/economy/OriginalLoanRules.Tests.csproj': [],
    'lanes/guests/rules/GuestOriginalRules.Tests.csproj': [],
    'lanes/clock/OriginalSchedulerRules/OriginalSchedulerRules.csproj': [],
    'lanes/economy/OriginalScientistSnapshot.Tests.csproj': [],
    'lanes/rides/animation/AnimationWitness.csproj': [],
    'lanes/ui/csharp/OriginalLayoutReader.csproj': ['--self-test'],
}
# Harness -> option that takes the --pc-fixture path; added only when --pc-fixture is given.
FIXTURE_ARGUMENTS = {
    'lanes/economy/OriginalScientistSnapshot.Tests.csproj': '--fixture',
}
NEEDS_CORPUS = {
    'lanes/advisor/layer1/Layer1Corpus.csproj': 'Layer I corpus comparison (private corpus, long run)',
    'lanes/rides/CorpusWitness.csproj': 'PC data witness (private install)',
}


class FixtureError(Exception):
    pass


def check_pc_fixture(path: Path, repo: Path | None) -> Path:
    if not path.is_file():
        raise FixtureError(f'--pc-fixture is not a regular file: {path}')
    if path.suffix.lower() != PC_FIXTURE_SUFFIX:
        raise FixtureError(f'--pc-fixture must be the Easymode.TPWI save container, not {path.name}')
    resolved = path.resolve()
    if repo is not None and resolved.is_relative_to(repo.resolve()):
        raise FixtureError(f'--pc-fixture lies inside the checkout; original assets stay outside Git: {path}')
    size = resolved.stat().st_size
    if not PC_FIXTURE_HEADER_BYTES < size <= PC_FIXTURE_MAX_BYTES:
        raise FixtureError(f'--pc-fixture size {size} is outside ({PC_FIXTURE_HEADER_BYTES:#x}, {PC_FIXTURE_MAX_BYTES}]')
    return resolved


def fixture_environment(base: dict, mac_bin: Path | None, pc_data: Path | None,
                        pc_fixture: Path | None = None, repo: Path | None = None) -> dict:
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
    if pc_fixture is not None:
        # Rides and economy read OPENTPW_PC_FIXTURE; clock's saved-epoch witnesses read OPENTPW_PPC_SAVE_PATH.
        env['OPENTPW_PC_FIXTURE'] = env['OPENTPW_PPC_SAVE_PATH'] = str(check_pc_fixture(pc_fixture, repo))
    return env


def environment_read(variable: str) -> re.Pattern:
    name = re.escape(variable)
    return re.compile(rf'(?:environ\.get|environ|getenv)\s*[\[(]\s*[\'"]{name}[\'"]')


def fixture_consumers(tools: Path, variable: str, dotnet: list[dict] = ()) -> list[str]:
    # Python sources that read the variable from the environment, plus fixture-argument harnesses
    # that ran with it. A present harness that never ran consumes nothing. Only ppc-analysis
    # sources are read; no fixture directory is listed.
    read = environment_read(variable)
    runner = Path(__file__).resolve()
    sources = [str(file.relative_to(tools)) for file in sorted(tools.rglob('*.py'))
               if '__pycache__' not in file.parts and file.resolve() != runner
               and read.search(file.read_text(encoding='utf-8', errors='replace'))]
    harnesses = [item['project'] for item in dotnet if variable == 'OPENTPW_PC_FIXTURE' and used_fixture(item)]
    return sources + harnesses


def used_fixture(item: dict) -> bool:
    option = FIXTURE_ARGUMENTS.get(item['project'])
    return (option is not None and item.get('status') == 'passed' and not item.get('fixture_skips')
            and option in item.get('arguments', []))


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


def linked_outside_copy(project: Path) -> list[str]:
    # run_dotnet copies only project.parent; any include that climbs out of it is missing there.
    found = []
    for kind, include in ITEM.findall(project.read_text(encoding='utf-8')):
        depth = 0
        for part in PROJECT_DIRECTORY.sub('', include).replace('\\', '/').split('/'):
            depth += -1 if part == '..' else 0 if part in ('', '.') else 1
            if depth < 0:
                found.append(f'{kind} {include}')
                break
    return found


def not_run_reason(key: str, project: Path) -> str:
    if key in NEEDS_CORPUS:
        return NEEDS_CORPUS[key]
    linked = linked_outside_copy(project)
    return 'no registered self-test' + (f"; scratch copy would lack {', '.join(linked)}" if linked else '')


def dotnet_harnesses(tools: Path) -> list[tuple[str, Path]]:
    return sorted((str(path.relative_to(tools)), path) for path in (tools / 'lanes').rglob('*.csproj')
                  if not {'bin', 'obj'} & set(path.parts))


def absent_harnesses(tools: Path) -> list[dict]:
    return [{'project': key, 'framework': None, 'status': 'absent',
             'reason': 'registered, not in this checkout; no coverage claimed'}
            for key in sorted({*SELF_TESTS, *NEEDS_CORPUS}) if not (tools / key).is_file()]


def harness_arguments(key: str, env: dict) -> list[str]:
    arguments = list(SELF_TESTS[key])
    if key in FIXTURE_ARGUMENTS and env.get('OPENTPW_PC_FIXTURE'):
        arguments += [FIXTURE_ARGUMENTS[key], env['OPENTPW_PC_FIXTURE']]
    return arguments


def run_dotnet(dotnet: Path, key: str, project: Path, env: dict | None = None,
               require_fixtures: bool = False) -> dict:
    result = {'project': key, 'framework': project_framework(project)}
    incompatible = incompatible_references(project)
    if incompatible:
        return {**result, 'status': 'incompatible-reference', 'reason': '; '.join(incompatible)}
    if key in NEEDS_CORPUS or key not in SELF_TESTS:
        return {**result, 'status': 'not-run', 'reason': not_run_reason(key, project)}
    linked = linked_outside_copy(project)
    if linked:
        return {**result, 'status': 'linked-outside-copy', 'reason': '; '.join(linked)}
    fixtures = env if env is not None else fixture_environment(dict(os.environ), None, None)
    arguments = harness_arguments(key, fixtures)
    env = {**fixtures, 'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1',
           'DOTNET_ROOT': str(dotnet.resolve().parent)}
    with tempfile.TemporaryDirectory(prefix='ppc-harness-') as scratch:
        copy = Path(scratch) / project.parent.name
        shutil.copytree(project.parent, copy, ignore=shutil.ignore_patterns('bin', 'obj'))
        version = subprocess.run([str(dotnet), '--version'], cwd=scratch, env=env,
                                 capture_output=True, text=True, timeout=60).stdout.strip()
        process = subprocess.run([str(dotnet), 'run', '--project', str(copy / project.name), '--', *arguments],
                                 cwd=scratch, env=env, capture_output=True, text=True, timeout=600)
    output = process.stdout + process.stderr
    lines = [line for line in process.stdout.splitlines() if line.strip()]
    skipped = [line.strip() for line in lines if line.strip().startswith('NOT RUN:')]
    status = 'passed' if process.returncode == 0 else classify_dotnet_failure(output)
    if status == 'passed' and require_fixtures and skipped:
        status = 'fixture-skipped'
    return {**result, 'sdk': version, 'status': status, 'arguments': arguments,
            'fixture_skips': skipped, 'last_line': lines[-1] if lines else ''}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--mac-bin', type=Path)
    parser.add_argument('--pc-data', type=Path)
    parser.add_argument('--pc-fixture', type=Path, help='identified Data/levels/jungle/Easymode.TPWI (outside Git)')
    parser.add_argument('--require-fixtures', action='store_true')
    parser.add_argument('--dotnet', type=Path)
    parser.add_argument('--json', action='store_true')
    args = parser.parse_args(argv)

    tools = args.repo.resolve() / 'tools' / 'ppc-analysis'
    try:
        env = fixture_environment(dict(os.environ), args.mac_bin, args.pc_data, args.pc_fixture, args.repo)
    except FixtureError as error:
        print(f'fixture error: {error}', file=sys.stderr)
        return 2
    suites = python_suites(tools)
    harnesses = dotnet_harnesses(tools)
    if args.dotnet:
        dotnet = [run_dotnet(args.dotnet, key, path, env, args.require_fixtures) for key, path in harnesses]
    else:
        dotnet = [{'project': key, 'framework': project_framework(path), 'status': 'not-run',
                   'reason': 'no --dotnet' if key in SELF_TESTS and key not in NEEDS_CORPUS else
                   not_run_reason(key, path)} for key, path in harnesses]
    consumers = {key: fixture_consumers(tools, key, dotnet) for key in FIXTURE_VARIABLES if key in env}
    unconsumed = [flag for flag, keys in FIXTURE_FLAGS.items()
                  if keys[0] in env and not any(consumers.get(key) for key in keys)]
    report = {
        'fixtures': {key: env.get(key) for key in FIXTURE_VARIABLES},
        'fixture_consumers': consumers,
        'unconsumed_fixtures': unconsumed,
        'uncovered': uncovered_test_files(tools, suites),
        'python': [run_python(suite, tools, env, args.require_fixtures) for suite in suites],
        'dotnet': dotnet + absent_harnesses(tools),
    }
    failed = (bool(report['uncovered']) or any(not suite['passed'] for suite in report['python'])
              or any(item['status'] not in ('passed', 'not-run', 'absent') for item in report['dotnet'])
              or (args.require_fixtures and bool(report['unconsumed_fixtures'])))

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
        for key in report['unconsumed_fixtures']:
            mark = 'FAIL' if args.require_fixtures else 'note'
            print(f"{mark} {key} ({', '.join(FIXTURE_FLAGS[key])}) is set but nothing that ran in this checkout used it")
        for item in report['dotnet']:
            mark = 'ok  ' if item['status'] in ('passed', 'not-run', 'absent') else 'FAIL'
            detail = '; '.join(item.get('fixture_skips') or []) or item.get('last_line') or item.get('reason', '')
            print(f"{mark} dotnet {item['project']} ({item['framework']}, sdk {item.get('sdk', '-')}): {item['status']} {detail}")
        total = sum(suite['ran'] for suite in report['python'])
        skipped = sum(suite['skipped'] for suite in report['python'])
        print(f"{'FAILED' if failed else 'OK'}: {len(suites)} Python suites, {total} tests, {skipped} skipped")
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
