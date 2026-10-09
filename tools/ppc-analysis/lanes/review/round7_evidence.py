"""Round-7 review witnesses: clock/advisor operands and integration-tree hygiene.

Usage: python3 -I round7_evidence.py [--bin-root /path/to/mac-feral/bin] [--repo /path/to/checkout ...]

--bin-root pins SimThemePark.data and re-decodes operands cited by clock d66857e (controller
clock accessor and its literal-zero caller) and advisor be46ebe (EVENT/EVENT_EXT dispatch and
SPAWNSOUND child store). The rides selector reading is round 6's (round6_evidence.py); it is not
repeated here. --repo inspects a checkout without building it: projects that still target a
framework other than the pinned SDK major, Python test files that CI's top-level discovery cannot
reach, and home-directory paths in English docs. Output is counts, paths and conclusions only.
Nothing original is executed.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import review_evidence as rv  # noqa: E402
from review_evidence import Binary, ReviewError  # noqa: E402


def clock_and_advisor_audit(app: Binary) -> dict:
    # 0xa3e08: cmpwi r3,0; lwz r3,TOC; beq -> +0x4010 (16400, scaled); else +0x4018 (16408, unscaled).
    app.expect(0xa3e08, 'cmpi', 0, 3, 0)
    app.expect(0xa3e10, 'bc', 12, 2, 0xa3e1c)
    app.expect(0xa3e14, 'lwz', 3, 3, 16408)
    app.expect(0xa3e1c, 'lwz', 3, 3, 16400)
    # Coaster tick passes literal zero, then calls the accessor.
    app.expect(0x406bc, 'addi', 3, 0, 0)
    rv.require(app.call(0x406c4), 0xa3e08, 'coaster tick clock call')
    # EVENT passes literal 1000 as r7; EVENT_EXT passes an operand value in r7.
    app.expect(0xaf9e4, 'addi', 7, 0, 1000)
    rv.require(app.call(0xaf9e8), 0xae930, 'EVENT dispatcher call')
    app.expect(0xafa4c, 'addi', 7, 3, 0)
    rv.require(app.call(0xafa60), 0xae930, 'EVENT_EXT dispatcher call')
    # SPAWNSOUND stores the child script ID at parent +20.
    app.expect(0xb12a0, 'stw', 3, 31, 20)
    return {
        'controller_clock': 'argument 0 -> +16400 (scaled cache), nonzero -> +16408; coaster tick passes 0',
        'event_dispatch': 'EVENT r7=1000; EVENT_EXT r7 from operand; both call 0xae930',
        'spawnsound': 'child id stored at parent +20',
    }


FRAMEWORK = re.compile(r'<TargetFramework>\s*net(\d+)\.\d+\s*</TargetFramework>')
HOME = re.compile(r'/Users/[A-Za-z0-9._-]+/|/home/[A-Za-z0-9._-]+/')


def sdk_major(repo: Path) -> int | None:
    path = repo / 'global.json'
    if not path.exists():
        return None
    version = json.loads(path.read_text())['sdk']['version']
    return int(version.split('.')[0])


def framework_mismatches(repo: Path, major: int | None) -> list[str]:
    out = []
    for project in sorted(repo.rglob('*.csproj')):
        if any(part in ('bin', 'obj') for part in project.parts):
            continue
        found = FRAMEWORK.search(project.read_text(errors='replace'))
        if found and major is not None and int(found.group(1)) != major:
            out.append(f'{project.relative_to(repo)}: net{found.group(1)}')
    return out


def undiscovered_tests(repo: Path, start: str = 'tools/ppc-analysis') -> list[str]:
    """unittest discover recurses only into packages; a test below a non-package directory is skipped."""
    root = repo / start
    out = []
    for test in sorted(root.rglob('test_*.py')):
        parent = test.parent
        reachable = True
        while parent != root:
            if not (parent / '__init__.py').exists():
                reachable = False
                break
            parent = parent.parent
        if not reachable:
            out.append(str(test.relative_to(repo)))
    return out


def home_paths(repo: Path) -> list[str]:
    out = []
    for doc in sorted((repo / 'docs').rglob('*.md')):
        for number, line in enumerate(doc.read_text(errors='replace').splitlines(), 1):
            if HOME.search(line):
                out.append(f'{doc.relative_to(repo)}:{number}')
    return out


def repo_audit(repo: Path) -> dict:
    major = sdk_major(repo)
    mismatched = framework_mismatches(repo, major)
    hidden = undiscovered_tests(repo)
    homes = home_paths(repo)
    return {
        'sdk_major': major,
        'framework_mismatches': mismatched,
        'undiscovered_lane_tests': len(hidden),
        'undiscovered_examples': hidden[:5],
        'doc_home_path_lines': len(homes),
        'doc_home_path_files': sorted({line.rsplit(':', 1)[0] for line in homes}),
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bin-root', type=Path)
    parser.add_argument('--repo', type=Path, action='append', default=[])
    args = parser.parse_args()
    if not args.bin_root and not args.repo:
        parser.error('give --bin-root and/or --repo')
    result = {}
    try:
        if args.bin_root:
            app = Binary(args.bin_root, 'SimThemePark.data', 'SimThemePark.data', rv.APP_TOC)
            result['clock_advisor_operands'] = clock_and_advisor_audit(app)
        for repo in args.repo:
            result[f'repo:{repo.name}'] = repo_audit(repo)
    except (OSError, ValueError, KeyError, rv.pef.PEFError, ReviewError) as error:
        parser.exit(1, f'round7 evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()
