"""Round 13 review aids for run_evidence_checks.py: fixture consumers that never ran, and harness
items that a project-directory scratch copy cannot reach.

Usage: python3 -I round13_evidence.py --project LANE.csproj [--project ...]
       python3 -I round13_evidence.py --report RUNNER_JSON

Reads only the named project files or a runner --json report; never lists an asset directory.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ITEM = re.compile(r'<(Compile|ProjectReference|None|Content|EmbeddedResource)\s+[^>]*?Include="([^"]+)"')


def linked_outside_copy(project: Path) -> list[str]:
    # run_dotnet copies only project.parent; any include that climbs out of it is missing there.
    found = []
    for kind, include in ITEM.findall(project.read_text(encoding='utf-8')):
        parts = include.replace('\\', '/').split('/')
        depth = 0
        for part in parts:
            depth += -1 if part == '..' else 0 if part in ('', '.') else 1
            if depth < 0:
                found.append(f'{kind} {include}')
                break
    return found


def vacuous_fixture_consumers(report: dict, fixture_arguments: dict[str, str]) -> list[str]:
    # A fixture variable whose only consumers are fixture-argument harnesses that did not run with
    # the fixture is "consumed" by the runner's count, yet nothing in the run read it.
    ran = {item['project'] for item in report.get('dotnet', [])
           if item.get('status') == 'passed' and fixture_arguments.get(item['project']) in item.get('arguments', [])}
    vacuous = []
    for variable, consumers in report.get('fixture_consumers', {}).items():
        if not consumers:
            continue
        python = [entry for entry in consumers if entry.endswith('.py')]
        if not python and not set(consumers) & ran:
            vacuous.append(variable)
    return sorted(vacuous)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--project', type=Path, action='append', default=[])
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    result: dict = {'linked_outside_copy': {str(p): linked_outside_copy(p) for p in args.project}}
    if args.report:
        sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
        import run_evidence_checks as runner
        result['vacuous_fixture_consumers'] = vacuous_fixture_consumers(
            json.loads(args.report.read_text(encoding='utf-8')), runner.FIXTURE_ARGUMENTS)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
